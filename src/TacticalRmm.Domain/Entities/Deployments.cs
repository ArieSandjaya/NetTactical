namespace TacticalRmm.Domain.Entities;

/// <summary>A shareable install link for one client/site. Whoever opens it gets a PowerShell script that installs the agent there, until it expires
/// or its use limit is reached. Same idea as Tactical RMM's deployments, without building an installer file.</summary>
public class Deployment
{
    public int Id { get; set; }
    /// <summary>128 random bits as 32 hex characters; this is the secret in the link.</summary>
    public string Uid { get; set; } = "";
    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;
    public string AgentType { get; set; } = "workstation";
    public string Arch { get; set; } = "amd64";
    public bool Rdp { get; set; }
    public bool Ping { get; set; }
    public bool Power { get; set; }
    public int? NatsPort { get; set; }
    /// <summary>The server address agents will use, fixed when the link is made.</summary>
    public string Api { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int Uses { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
