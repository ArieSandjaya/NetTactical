using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Application.Logic;

/// <summary>What the overview shows on top of the health score: the share of devices that are fine per category, and a hardware and warranty summary. Pure, no database.</summary>
public static class FleetInsights
{
    public sealed record Device(HealthInput Health, bool Windows, HardwareSnapshot? Hardware);

    public static IReadOnlyList<CategoryScoreDto> Categories(IReadOnlyCollection<Device> devices, HealthWeights w)
    {
        static CategoryScoreDto Make(string code, IEnumerable<bool> results)
        {
            var l = results.ToList();
            var good = l.Count(x => x);
            return new CategoryScoreDto(code, l.Count, good, l.Count == 0 ? null : (int)Math.Round(100.0 * good / l.Count));
        }
        var hasMetric = devices.Where(d => d.Health.Cpu is not null || d.Health.Ram is not null || d.Health.DiskWorstUsed is not null).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return
        [
            Make("availability", devices.Select(d => d.Health.Online)),
            Make("patching", devices.Select(d => d.Health.PendingUpdates == 0 && !d.Health.NeedsReboot)),
            Make("alerts", devices.Select(d => d.Health.ErrorAlerts + d.Health.WarningAlerts == 0)),
            Make("resources", hasMetric.Select(d => (d.Health.DiskWorstUsed ?? 0) < 85 && (d.Health.Cpu ?? 0) < 80 && (d.Health.Ram ?? 0) < 80)),
            Make("hardware", devices.Where(d => d.Hardware is not null).Select(d => HardwareHealth.Assess(d.Hardware, null, today).Risk == HardwareHealth.Ok)),
            Make("agent", devices.Select(d => !d.Health.AgentOutdated)),
        ];
    }

    public sealed record HardwareItem(string AgentId, string Hostname, string Client, string Site, bool Windows, HardwareSnapshot? Snapshot, DateOnly? WarrantyEnd, DateTime? CollectedAt);

    public static FleetHardware Hardware(IReadOnlyCollection<HardwareItem> items, DateOnly today, int limit)
    {
        var assessed = items.Where(i => i.Snapshot is not null || i.WarrantyEnd is not null)
            .Select(i => (Item: i, A: HardwareHealth.Assess(i.Snapshot, i.WarrantyEnd, today))).ToList();
        var checkedItems = items.Where(i => i.Snapshot is not null).ToList();

        // Disk and battery problems are told apart by assessing each part on its own.
        bool DiskBad(HardwareSnapshot s) => HardwareHealth.Assess(s with { Battery = null }, null, today).Risk != HardwareHealth.Ok;
        bool BatteryBad(HardwareSnapshot s) => HardwareHealth.Assess(s with { Disks = [] }, null, today).Risk != HardwareHealth.Ok;

        var models = checkedItems.Select(i => string.Join(' ', new[] { i.Snapshot!.Manufacturer, i.Snapshot.Model }.Where(x => !string.IsNullOrWhiteSpace(x))))
            .Where(m => m.Length > 0).GroupBy(m => m).Select(g => new CountRow(g.Key, g.Count())).OrderByDescending(c => c.Count).ThenBy(c => c.Name).Take(8).ToList();

        var atRisk = assessed.Where(a => a.A.Risk is HardwareHealth.High or HardwareHealth.Watch)
            .OrderBy(a => a.A.Risk == HardwareHealth.High ? 0 : 1).ThenBy(a => a.Item.Hostname, StringComparer.OrdinalIgnoreCase).Take(limit)
            .Select(a => new HardwareRowDto(a.Item.AgentId, a.Item.Hostname, a.Item.Client, a.Item.Site, a.Item.Snapshot?.Manufacturer, a.Item.Snapshot?.Model, a.Item.Snapshot?.Serial,
                a.A.Risk, a.A.Reasons, a.Item.WarrantyEnd, a.Item.CollectedAt)).ToList();

        int Left(HardwareItem i) => i.WarrantyEnd!.Value.DayNumber - today.DayNumber;
        return new FleetHardware(
            checkedItems.Count, items.Count(i => i.Windows && i.Snapshot is null),
            assessed.Count(a => a.Item.Snapshot is not null && a.A.Risk == HardwareHealth.Ok),
            assessed.Count(a => a.Item.Snapshot is not null && a.A.Risk == HardwareHealth.Watch),
            assessed.Count(a => a.Item.Snapshot is not null && a.A.Risk == HardwareHealth.High),
            checkedItems.Count(i => DiskBad(i.Snapshot!)), checkedItems.Count(i => BatteryBad(i.Snapshot!)),
            items.Count(i => i.WarrantyEnd is not null && Left(i) < 0),
            items.Count(i => i.WarrantyEnd is not null && Left(i) is >= 0 and <= HardwareHealth.WarrantySoonDays),
            items.Count(i => i.Windows && i.WarrantyEnd is null),
            models, atRisk);
    }
}
