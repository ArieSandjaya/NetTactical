namespace TacticalRmm.Application.Dtos;

public sealed record DiskInfo(string? Name, string? Media, long? SizeBytes, string? Health, string? Status, int? TemperatureC, int? WearPercent, long? ReadErrors, long? WriteErrors, long? PowerOnHours);
public sealed record BatteryInfo(long? DesignMwh, long? FullMwh, int? Cycles);
public sealed record HardwareSnapshot(string? Manufacturer, string? Model, string? Serial, string? BiosVersion, string? BiosDate, IReadOnlyList<DiskInfo> Disks, BatteryInfo? Battery);

/// <summary>Level is "high" or "watch".</summary>
public sealed record HardwareReasonDto(string Level, string Text);

/// <summary>Risk is "ok", "watch", "high" or "unknown" (never checked).</summary>
public sealed record HardwareDto(string AgentId, string Hostname, DateTime? CollectedAt, string Risk, IReadOnlyList<HardwareReasonDto> Reasons, HardwareSnapshot? Snapshot,
    DateOnly? WarrantyEnd, string? WarrantyNote, string? VendorLink);

public sealed record HardwareRowDto(string AgentId, string Hostname, string Client, string Site, string? Manufacturer, string? Model, string? Serial, string Risk,
    IReadOnlyList<HardwareReasonDto> Reasons, DateOnly? WarrantyEnd, DateTime? CollectedAt);

public sealed class WarrantyRequest
{
    /// <summary>yyyy-MM-dd; empty clears the date.</summary>
    public string? EndDate { get; set; }
    public string? Note { get; set; }
}
