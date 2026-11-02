using System.Security.Cryptography;
using System.Text;

namespace SowSiege.Server.Infrastructure;

public sealed record SocialAuthOptions(IReadOnlyList<SocialProviderOptions> Providers, byte[]? CredentialKey = null)
{
    public override string ToString() => "SocialAuthOptions { credentials redacted }";
}

public sealed record SocialChallenge(Guid ChallengeId, string Nonce, DateTimeOffset ExpiresAt);
public sealed record SocialAuthReply(bool Linked, AuthTokens? Tokens = null, DateTimeOffset? PurgeAt = null);

/// <summary>Purpose-bound provider proofs. All provider I/O happens outside account transactions.</summary>
public sealed class SocialAuthService(AuthStore auth, ISocialIdentityVerifier verifier, IAppleTokenClient apple, SocialAuthOptions options)
{
    private readonly SocialProviderOptions[] providers = options.Providers.ToArray();
    private readonly byte[]? encryptionKey = options.CredentialKey?.ToArray();

    public Task<SocialChallenge> ChallengeAsync(string provider, string clientId, string purpose, string? accessToken, byte[]? publicKey,
        string clientIp, CancellationToken cancellation)
    {
        RequireProvider(provider, clientId);
        return auth.CreateSocialChallengeAsync(provider, clientId, purpose, accessToken, publicKey, clientIp, cancellation);
    }

    public async Task<SocialAuthReply> CompleteAsync(Guid challengeId, string nonce, string purpose, string identityToken, string? authorizationCode,
        byte[]? signature, string? accessToken, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(identityToken) || identityToken.Length > 16_384) { throw new AuthException("social-invalid"); }
        var challenge = await auth.ConsumeSocialChallengeAsync(challengeId, nonce, purpose, cancellation);
        var provider = RequireProvider(challenge.Provider, challenge.ClientId);
        if (purpose == "login")
        {
            if (challenge.PublicKey is null || signature is null || signature.Length != 64) { throw new AuthException("social-invalid"); }
            try
            {
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(challenge.PublicKey, out _);
                if (!key.VerifyData(SignedBytes(challengeId, nonce), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                {
                    throw new AuthException("social-invalid");
                }
            }
            catch (CryptographicException) { throw new AuthException("social-invalid"); }
        }
        else if (signature is not null) { throw new AuthException("social-invalid"); }

        var identity = await verifier.VerifyAsync(challenge.Provider, challenge.ClientId, identityToken, nonce, cancellation);
        if (identity.Provider != challenge.Provider || identity.ClientId != challenge.ClientId) { throw new AuthException("social-invalid"); }
        AuthStore.AppleCredential? credential = null;
        if (challenge.Provider == "apple")
        {
            if (string.IsNullOrWhiteSpace(authorizationCode) || authorizationCode.Length > 8192) { throw new AuthException("social-invalid"); }
            var exchanged = await apple.ExchangeAsync(challenge.ClientId, authorizationCode, provider.RedirectUri, cancellation);
            var returned = await verifier.VerifyAsync(challenge.Provider, challenge.ClientId, exchanged.IdentityToken, nonce, cancellation);
            if (returned != identity) { throw new AuthException("social-invalid"); }
            credential = Protect(identity, exchanged.RefreshToken);
        }
        else if (authorizationCode is not null) { throw new AuthException("social-invalid"); }

        if (purpose == "link")
        {
            await auth.LinkSocialIdentityAsync(challenge, identity, accessToken ?? "", credential, cancellation);
            return new(true);
        }
        var used = await auth.UseSocialIdentityAsync(challenge, identity, credential, cancellation);
        if (purpose == "login") { return new(false, used.Tokens); }
        DateTimeOffset purgeAt = await auth.DeleteAccountAsync(used.Account, cancellation);
        return new(false, PurgeAt: purgeAt);
    }

    public static byte[] SignedBytes(Guid challengeId, string nonce) =>
        Encoding.UTF8.GetBytes("sow-social-login-v1\n" + challengeId.ToString("D") + "\n" + nonce);

    /// <summary>Only the revocation row is locked during bounded provider I/O; account and alliance rows are never locked here.</summary>
    public Task<int> RetryRevocationsAsync(CancellationToken cancellation)
    {
        if (encryptionKey is not { Length: 32 }) { return Task.FromResult(0); }
        return auth.ExecuteAppleRevocationsAsync(async (credential, token) =>
        {
            RequireProvider("apple", credential.ClientId);
            await apple.RevokeAsync(credential.ClientId, Unprotect(credential), token);
        }, cancellation);
    }

    private SocialProviderOptions RequireProvider(string provider, string clientId)
    {
        if (provider is not ("apple" or "google") || providers.SingleOrDefault(p => p.Provider == provider && p.ClientId == clientId) is not { } configured)
        {
            throw new AuthException("social-disabled");
        }
        if (provider == "apple" && encryptionKey is not { Length: 32 }) { throw new AuthException("social-disabled"); }
        return configured;
    }

    private AuthStore.AppleCredential Protect(VerifiedSocialIdentity identity, string refresh)
    {
        if (encryptionKey is not { Length: 32 }) { throw new AuthException("social-disabled"); }
        var id = Guid.NewGuid();
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plaintext = Encoding.UTF8.GetBytes(refresh);
        byte[] envelope = new byte[12 + 16 + plaintext.Length];
        nonce.CopyTo(envelope, 0);
        try
        {
            using var aes = new AesGcm(encryptionKey, 16);
            aes.Encrypt(nonce, plaintext, envelope.AsSpan(28), envelope.AsSpan(12, 16), id.ToByteArray());
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        string reference = Convert.ToHexString(HMACSHA256.HashData(encryptionKey, Encoding.UTF8.GetBytes("apple-subject-v1\n" + identity.Subject)));
        return new(id, reference, identity.ClientId, envelope);
    }

    private string Unprotect(AuthStore.AppleCredential credential)
    {
        if (encryptionKey is not { Length: 32 } || credential.Encrypted.Length < 28) { throw new CryptographicException(); }
        byte[] plaintext = new byte[credential.Encrypted.Length - 28];
        try
        {
            using var aes = new AesGcm(encryptionKey, 16);
            aes.Decrypt(credential.Encrypted.AsSpan(0, 12), credential.Encrypted.AsSpan(28), credential.Encrypted.AsSpan(12, 16), plaintext, credential.Id.ToByteArray());
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
