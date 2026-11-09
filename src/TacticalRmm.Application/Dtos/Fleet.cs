namespace TacticalRmm.Application.Dtos;

// All DTOs are serialised as snake_case by the API (see Program.cs).

public record HealthFactorDto(string Code, int Penalty, string Detail);
public record HealthDto(int Score, string Level, IReadOnlyList<HealthFactorDto> Factors);

public record TrendPoint(DateTime Date, double Avg, int Samples);

public record FleetScope(int? ClientId, int? SiteId, string Name);
public record FleetTotals(int Devices, int Online, int Offline, int AvgHealth, string Level, int AtRisk);
public record FleetAlerts(int Error, int Warning, int Info);
public record FleetPatch(int UpToDate, int Few, int Many, int PendingDevices, int NeedsReboot);
public record FleetClientRow(int Id, string Name, int Devices, int AvgHealth, int AtRisk);
public record FleetRiskRow(string AgentId, string Hostname, string Client, string Site, string Os, int Score, string Level, IReadOnlyList<HealthFactorDto> Reasons, int? UptimeDays, bool Online);
public record FleetAlertRow(int Id, string AgentId, string Hostname, string Client, string Severity, string? Message, DateTime Time);
public record CountRow(string Name, int Count);
public record FleetAgentVersions(string Latest, int Current, int Outdated);

public record FleetSummaryDto(
    FleetScope Scope, FleetTotals Totals, FleetAlerts Alerts, FleetPatch Patch,
    IReadOnlyList<FleetClientRow> Clients, IReadOnlyList<FleetRiskRow> AtRisk,
    IReadOnlyList<FleetAlertRow> RecentAlerts, IReadOnlyList<CountRow> OperatingSystems,
    FleetAgentVersions AgentVersions,
    IReadOnlyDictionary<string, IReadOnlyList<TrendPoint>> Trends);

/// <summary>One asset in the asset list, with its health score and the main reason for it.</summary>
public record FleetAssetRow(string AgentId, string Hostname, string Client, string Site, string Os, string? Version, int Score, string Level, bool Online,
    DateTime? LastSeen, int PendingUpdates, int OpenAlerts, bool NeedsReboot, int? UptimeDays, string? TopReason);
public record FleetAssetsDto(FleetScope Scope, IReadOnlyList<FleetAssetRow> Assets);

public record PatchTotals(int PendingUpdates, int FailedUpdates, int DevicesAffected, int NeedsReboot);
/// <summary>One update (grouped by KB) and how many assets it is outstanding on.</summary>
public record PatchUpdateRow(string? Kb, string? Title, string? Severity, string Status, int Devices);
public record PatchDeviceRow(string AgentId, string Hostname, string Client, int Pending, int Failed, bool NeedsReboot, bool Online);
public record FleetPatchesDto(FleetScope Scope, PatchTotals Totals, IReadOnlyList<PatchUpdateRow> Updates, IReadOnlyList<PatchDeviceRow> Devices);

public record MetricPoint(DateTime Time, double Value);
public record MetricSeries(string Metric, string? Label, int Days, IReadOnlyList<MetricPoint> Points);

public record DiskDto(string Device, string? Fstype, string? Total, string? Used, string? Free, double Percent);
public record ServiceIssueDto(string Name, string? DisplayName, string Status, string? StartType);

public record AgentFactsDto(
    string AgentId, string Hostname, string Client, string Site, string Os, string Plat, string? Version, bool AgentOutdated,
    DateTime? LastSeen, bool Online, string? PublicIp, string? LoggedInUser, int? UptimeDays, bool NeedsReboot,
    bool MaintenanceMode, string? Description, double? TotalRamGb, string? Goarch);

public record AgentMetricsDto(double? Cpu, double? Memory, double? DiskWorst);

/// <summary>One open issue on a device: a combination of failed checks, failed tasks, availability, patches and agent version.</summary>
public record IssueDto(
    string Kind, string Severity, string Title, string Source,
    DateTime? FirstSeen, DateTime? LastSeen, int Count, string? ReturnCode, string? Output,
    int? RefId);

public record AgentCheckRow(int Id, string Name, string Type, string Status, DateTime? LastRun, int RunInterval, string? Info, bool ManagedByPolicy);
public record AgentTaskRow(int Id, string Name, string Type, bool Enabled, string Status, DateTime? LastRun, string SyncStatus, bool ManagedByPolicy);
public record AgentPatchRow(string? Kb, string? Title, string? Severity, string Status);

public record AgentOverviewDto(
    AgentFactsDto Agent, HealthDto Health, AgentMetricsDto Metrics, IReadOnlyList<DiskDto> Disks,
    IReadOnlyList<IssueDto> Issues, IReadOnlyList<AgentCheckRow> Checks, IReadOnlyList<AgentTaskRow> Tasks,
    IReadOnlyList<AgentPatchRow> Patches, IReadOnlyList<ServiceIssueDto> Services);
