namespace TacticalRmm.Application.Dtos;

public class DeploymentRequest
{
    public int ClientId { get; set; }
    public int SiteId { get; set; }
    public string AgentType { get; set; } = "workstation";
    public string Arch { get; set; } = "amd64";
    public int ExpiresHours { get; set; } = 168;
    public bool Rdp { get; set; }
    public bool Ping { get; set; }
    public bool Power { get; set; }
    public int? NatsPort { get; set; }
    public string? Api { get; set; }
    /// <summary>How many computers may use the link; empty = no limit.</summary>
    public int? MaxUses { get; set; }
    public string? Note { get; set; }
}

public record DeploymentDto(int Id, string ClientName, string SiteName, string AgentType, string Arch, string Api, string Link, string Command,
    DateTime ExpiresAt, bool Expired, int Uses, int? MaxUses, bool Rdp, bool Ping, bool Power, int? NatsPort, string? Note, string? CreatedBy, DateTime CreatedAt);
