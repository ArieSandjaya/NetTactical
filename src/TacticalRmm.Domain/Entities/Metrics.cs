namespace TacticalRmm.Domain.Entities;

/// <summary>Satu titik metrik historis (CPU/memori/disk dalam persen terpakai) dari hasil check agent.</summary>
public class CheckMetric
{
    public long Id { get; set; }
    public int AgentId { get; set; }
    public int CheckId { get; set; }
    /// <summary>cpuload | memory | diskspace</summary>
    public string CheckType { get; set; } = "";
    public DateTime Time { get; set; }
    /// <summary>Persen terpakai, 0-100.</summary>
    public double Value { get; set; }
    /// <summary>Untuk disk: huruf drive (mis. "C:"). Selain itu null.</summary>
    public string? Label { get; set; }
}
