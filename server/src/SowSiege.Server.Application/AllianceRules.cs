using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SowSiege.Server.Application;

public sealed record AllianceChatRules(int MaxChars, int IntervalSeconds, int RetainedPerAlliance);
public sealed record AllianceHelpRules(int PerJobMaxGrants, int PerGrantMaxSeconds, int PerGrantPermille, int PerJobTotalPermille);

/// <summary>GDD19 alliance numbers from data/opt-in/slg/alliance-v1.json. All values are [가설].</summary>
public sealed record AllianceContent(int MemberLimit, int NameMinChars, int NameMaxChars, int IntroMaxChars, int NoticeMaxChars,
    int ApplicationHours, int RejoinHours, AllianceChatRules Chat, AllianceHelpRules Help, int ReportRetentionDays, string[] ReportReasons, string Hash)
{
    public const string DataPath = "opt-in/slg/alliance-v1.json";
    private static readonly JsonSerializerOptions Read = new(JsonSerializerDefaults.Web);

    private sealed record File(int MemberLimit, int NameMinChars, int NameMaxChars, int IntroMaxChars, int NoticeMaxChars,
        int ApplicationHours, int RejoinHours, AllianceChatRules Chat, AllianceHelpRules Help, int ReportRetentionDays, string[] ReportReasons);

    public static AllianceContent Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        return Parse(System.IO.File.ReadAllBytes(Path.Combine(data, DataPath)));
    }

    public static AllianceContent Parse(byte[] bytes)
    {
        var f = JsonSerializer.Deserialize<File>(bytes, Read) ?? throw new InvalidDataException("Alliance content missing.");
        if (f.MemberLimit < 2 || f.NameMinChars < 1 || f.NameMaxChars < f.NameMinChars || f.Chat is null || f.Help is null || f.ReportReasons is not { Length: > 0 } ||
            f.Chat.MaxChars < 1 || f.ReportRetentionDays < 1 || f.Chat.RetainedPerAlliance < 1 || f.Help.PerJobMaxGrants < 1 || f.Help.PerGrantMaxSeconds < 1 ||
            f.Help.PerGrantPermille is < 1 or > 1000 || f.Help.PerJobTotalPermille is < 1 or > 1000)
        {
            throw new InvalidDataException("Alliance content invalid.");
        }
        return new(f.MemberLimit, f.NameMinChars, f.NameMaxChars, f.IntroMaxChars, f.NoticeMaxChars, f.ApplicationHours, f.RejoinHours,
            f.Chat, f.Help, f.ReportRetentionDays, f.ReportReasons, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}

/// <summary>Pure alliance rules: text checks and the help shortening formula. No clock, I/O or RNG.</summary>
public static class AllianceRules
{
    /// <summary>NFC-normalised text, counted in user-perceived characters; null when it contains control or markup characters.</summary>
    public static string? Clean(string? text, int min, int max, bool allowEmpty = false)
    {
        if (text is null)
        {
            return null;
        }
        string value = text.Normalize(NormalizationForm.FormC).Trim();
        if (value.Length == 0)
        {
            return allowEmpty && min == 0 ? value : null;
        }
        if (value.Any(c => char.IsControl(c) || c is '<' or '>' || c == (char)0x200B || c == (char)0x2028 || c == (char)0x2029))
        {
            return null;
        }
        int count = new StringInfo(value).LengthInTextElements;
        return count >= Math.Max(min, 1) && count <= max ? value : null;
    }

    public static string NameKey(string name) => name.Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    /// <summary>
    /// GDD19 §6: one grant shortens by min(perGrantMax, floor(D0 * perGrant / 1000), R, floor(D0 * total / 1000) - H) milliseconds,
    /// counted in whole seconds; 0 means the grant is refused and consumes nothing.
    /// </summary>
    public static long HelpShortening(AllianceHelpRules rules, long d0Ms, long remainingMs, long givenMs)
    {
        long perGrant = d0Ms / 1000 * rules.PerGrantPermille / 1000 * 1000;
        long totalCap = d0Ms / 1000 * rules.PerJobTotalPermille / 1000 * 1000;
        long value = Math.Min(Math.Min(rules.PerGrantMaxSeconds * 1000L, perGrant), Math.Min(remainingMs / 1000 * 1000, totalCap - givenMs));
        return Math.Max(0, value);
    }
}
