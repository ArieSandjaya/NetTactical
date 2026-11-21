namespace TacticalRmm.Application.Logic;

/// <summary>What the server can clean up and how old a record must be before it goes. Pure rules, no database.</summary>
public static class ServerMaintenance
{
    public const string PruneAudit = "prune_audit", PruneAlerts = "prune_alerts", PruneHistory = "prune_history", PruneTokens = "prune_tokens", StaleAgents = "stale_agents";
    public const int MaxStaleAgents = 200;

    /// <summary>Action to the fewest days it may be given. A stale asset or the audit trail is never wiped by accident.</summary>
    public static readonly IReadOnlyDictionary<string, int> MinDays = new Dictionary<string, int>
    {
        [PruneAudit] = 30, [PruneAlerts] = 7, [PruneHistory] = 7, [PruneTokens] = 1, [StaleAgents] = 30,
    };

    public static string? Validate(string? action, int days)
    {
        if (action is null || !MinDays.TryGetValue(action, out var min)) return "Unknown maintenance action.";
        if (days < min) return $"Keep at least {min} days for this action.";
        return days > 3650 ? "Days must be 3650 or less." : null;
    }

    public static DateTime Cutoff(int days, DateTime now) => now.AddDays(-days);
}
