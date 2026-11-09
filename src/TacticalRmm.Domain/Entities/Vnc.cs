namespace TacticalRmm.Domain.Entities;

/// <summary>A VNC password the server may try when connecting to an asset. Lowest <see cref="Priority"/> is the default; the others are fallbacks.</summary>
public class VncCredential
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>AES-GCM protected password (never stored in plain text, never returned by the API).</summary>
    public string Secret { get; set; } = "";
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}
