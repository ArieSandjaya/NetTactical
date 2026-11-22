namespace TacticalRmm.Domain.Entities;

/// <summary>What the last hardware check found on an asset (serial, disk health, battery) plus the warranty date an administrator entered.
/// The risk is not stored: it is worked out when read, so a warranty running out changes it without another check.</summary>
public class HardwareInfo
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public DateTime? CollectedAt { get; set; }
    /// <summary>The snapshot as JSON; null until the first successful check.</summary>
    public string? Data { get; set; }
    public DateOnly? WarrantyEnd { get; set; }
    public string? WarrantyNote { get; set; }
}
