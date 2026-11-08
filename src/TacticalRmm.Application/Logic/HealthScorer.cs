namespace TacticalRmm.Application.Logic;

/// <summary>Score deduction weights (every value = points subtracted from 100). Configurable via Tactical:Health.</summary>
public class HealthWeights
{
    public int Offline { get; set; } = 45;
    public int DiskCritical { get; set; } = 25;      // >= 95%
    public int DiskHigh { get; set; } = 20;          // >= 90%
    public int DiskElevated { get; set; } = 10;      // >= 85%
    public int DiskWarning { get; set; } = 5;        // >= 80%
    public int CpuCritical { get; set; } = 12;       // >= 90%
    public int CpuElevated { get; set; } = 6;        // >= 80%
    public int RamCritical { get; set; } = 12;
    public int RamElevated { get; set; } = 6;
    public int PerErrorAlert { get; set; } = 8;
    public int ErrorAlertCap { get; set; } = 24;
    public int PerWarningAlert { get; set; } = 3;
    public int WarningAlertCap { get; set; } = 9;
    public int PatchesMany { get; set; } = 10;       // >= 10 pending updates
    public int PatchesSome { get; set; } = 7;        // >= 5
    public int PatchesFew { get; set; } = 3;         // >= 1
    public int NeedsReboot { get; set; } = 4;
    public int UptimeVeryLong { get; set; } = 5;     // > 180 days
    public int UptimeLong { get; set; } = 2;         // > 90 days
    public int AgentOutdated { get; set; } = 3;
    /// <summary>A score below this is considered at risk; below AttentionBelow = needs attention.</summary>
    public int AtRiskBelow { get; set; } = 60;
    public int AttentionBelow { get; set; } = 80;
}

public record HealthInput(
    bool Online,
    double? Cpu, double? Ram, double? DiskWorstUsed,
    int ErrorAlerts, int WarningAlerts,
    int PendingUpdates, bool NeedsReboot,
    int? UptimeDays, bool AgentOutdated);

/// <summary>One factor that lowers the score. Code is stable so the UI can map it to text.</summary>
public record HealthFactor(string Code, int Penalty, string Detail);

public record HealthResult(int Score, string Level, IReadOnlyList<HealthFactor> Factors);

public static class HealthScorer
{
    public const string Healthy = "healthy", Attention = "attention", AtRisk = "at_risk";

    public static string LevelOf(int score, HealthWeights w) =>
        score >= w.AttentionBelow ? Healthy : score >= w.AtRiskBelow ? Attention : AtRisk;

    public static HealthResult Score(HealthInput i, HealthWeights w)
    {
        var f = new List<HealthFactor>();
        void Add(string code, int penalty, string detail) { if (penalty > 0) f.Add(new HealthFactor(code, penalty, detail)); }

        if (!i.Online) Add("offline", w.Offline, "Agent offline");

        // Metrics are only scored when the agent is online: stale data from an offline agent is misleading.
        if (i.Online)
        {
            if (i.DiskWorstUsed is { } d)
            {
                var p = d >= 95 ? w.DiskCritical : d >= 90 ? w.DiskHigh : d >= 85 ? w.DiskElevated : d >= 80 ? w.DiskWarning : 0;
                Add("disk_high", p, $"Disk {Math.Round(d)}% used");
            }
            if (i.Cpu is { } c) Add("cpu_high", c >= 90 ? w.CpuCritical : c >= 80 ? w.CpuElevated : 0, $"CPU {Math.Round(c)}%");
            if (i.Ram is { } r) Add("ram_high", r >= 90 ? w.RamCritical : r >= 80 ? w.RamElevated : 0, $"Memory {Math.Round(r)}%");
        }

        Add("alerts_error", Math.Min(i.ErrorAlerts * w.PerErrorAlert, w.ErrorAlertCap), $"{i.ErrorAlerts} critical alerts");
        Add("alerts_warning", Math.Min(i.WarningAlerts * w.PerWarningAlert, w.WarningAlertCap), $"{i.WarningAlerts} warning alerts");

        var patch = i.PendingUpdates >= 10 ? w.PatchesMany : i.PendingUpdates >= 5 ? w.PatchesSome : i.PendingUpdates >= 1 ? w.PatchesFew : 0;
        Add("patches_pending", patch, $"{i.PendingUpdates} pending updates");
        if (i.NeedsReboot) Add("needs_reboot", w.NeedsReboot, "Reboot required");
        if (i.UptimeDays is { } u) Add("uptime_long", u > 180 ? w.UptimeVeryLong : u > 90 ? w.UptimeLong : 0, $"Uptime {u} days");
        if (i.AgentOutdated) Add("agent_outdated", w.AgentOutdated, "Agent version outdated");

        var score = Math.Clamp(100 - f.Sum(x => x.Penalty), 0, 100);
        return new HealthResult(score, LevelOf(score, w), f.OrderByDescending(x => x.Penalty).ToList());
    }
}
