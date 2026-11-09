namespace TacticalRmm.Application.Logic;

/// <summary>Pure rules for Windows patch listings (status mapping and severity ordering).</summary>
public static class PatchRules
{
    public const string Pending = "pending", Failed = "failed", Ignored = "ignored";

    /// <summary>Row status from the WinUpdate result/action fields.</summary>
    public static string StatusOf(string? result, string? action) =>
        result == "failed" ? Failed : action == "ignore" ? Ignored : Pending;

    /// <summary>Lower is more severe. Unknown severities sort last.</summary>
    public static int SeverityRank(string? severity) => severity?.ToLowerInvariant() switch
    {
        "critical" => 0,
        "important" => 1,
        "moderate" => 2,
        "low" => 3,
        _ => 4,
    };
}
