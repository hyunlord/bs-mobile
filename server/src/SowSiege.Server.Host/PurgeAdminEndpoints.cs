using System.Security.Cryptography;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public static class PurgeAdminEndpoints
{
    public static void MapPurgeAdmin(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Auth:PurgeAdmin:Enabled", false)) { return; }
        // Validate during endpoint registration; an enabled but broken configuration must fail startup.
        var credential = new PurgeAdminCredential(app.Configuration["Auth:PurgeAdmin:Secret"]);
        app.MapGet("/v1/admin/account-purge", async (HttpContext context, AuthStore auth, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var supplied = context.Request.Headers["X-Purge-Admin-Key"];
            if (supplied.Count != 1 || !credential.Accepts(supplied[0])) { return Results.Unauthorized(); }
            return Results.Ok(await auth.ReadPurgeOperationsAsync(ct));
        }).RequireRateLimiting("prototype");
    }
}

public sealed class PurgeAdminCredential
{
    private readonly byte[] key;
    public PurgeAdminCredential(string? configured)
    {
        if (!TryDecode(configured, out var decoded)) { throw new InvalidOperationException("Enabled purge administration requires a 32-byte hex secret."); }
        key = decoded;
    }

    public bool Accepts(string? supplied)
    {
        if (!TryDecode(supplied, out var decoded)) { return false; }
        try { return CryptographicOperations.FixedTimeEquals(key, decoded); }
        finally { CryptographicOperations.ZeroMemory(decoded); }
    }

    private static bool TryDecode(string? input, out byte[] bytes)
    {
        bytes = [];
        if (input is null || input.Length != 64) { return false; }
        try { bytes = Convert.FromHexString(input); return bytes.Length == 32; }
        catch (FormatException) { return false; }
    }
}
