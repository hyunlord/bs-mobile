using SowSiege.Server.Host;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class PurgeAdminCredentialTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-secret")]
    public void EnabledConfigurationRejectsMissingOrMalformedSecret(string? secret) =>
        Assert.Throws<InvalidOperationException>(() => new PurgeAdminCredential(secret));

    [Fact]
    public void SecretUsesDecodedBytesAndRejectsMalformedOrWrongCredentials()
    {
        string secret = new string('a', 64);
        var credential = new PurgeAdminCredential(secret);
        Assert.True(credential.Accepts(secret));
        Assert.True(credential.Accepts(secret.ToUpperInvariant()));
        Assert.False(credential.Accepts(new string('b', 64)));
        Assert.False(credential.Accepts(new string('g', 64)));
        Assert.False(credential.Accepts(secret + " "));
        Assert.False(credential.Accepts(null));
    }
}
