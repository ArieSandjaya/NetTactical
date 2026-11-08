namespace TacticalRmm.Domain.Entities;

/// <summary>Satu titik metrik historis (CPU/memory/disk as percent used) from an agent check result.</summary>
public class CheckMetric
{
    public long Id { get; set; }
    public int AgentId { get; set; }
    public int CheckId { get; set; }
    /// <summary>cpuload | memory | diskspace</summary>
    public string CheckType { get; set; } = "";
    public DateTime Time { get; set; }
    /// <summary>Percent used, 0-100.</summary>
    public double Value { get; set; }
    /// <summary>For disk: the drive letter (e.g. "C:"). Otherwise null.</summary>
    public string? Label { get; set; }
}
