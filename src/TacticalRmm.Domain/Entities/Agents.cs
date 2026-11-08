namespace TacticalRmm.Domain.Entities;

public class Agent : AuditableEntity
{
    public int Id { get; set; }
    public string Version { get; set; } = "0.1.0";
    public string? OperatingSystem { get; set; }
    public string Plat { get; set; } = AgentPlat.Windows;
    public string? Goarch { get; set; }
    public string Hostname { get; set; } = "";
    public string AgentId { get; set; } = "";
    public DateTime? LastSeen { get; set; }
    public string? Services { get; set; }   // jsonb, written by nats-api
    public string? PublicIp { get; set; }
    public int? TotalRam { get; set; }
    public string? Disks { get; set; }      // jsonb
    public double? BootTime { get; set; }
    public string? LoggedInUsername { get; set; }
    public string? LastLoggedInUser { get; set; }
    public string MonitoringType { get; set; } = AgentMonType.Server;
    public string? Description { get; set; }
    public string? MeshNodeId { get; set; }
    public bool OverdueEmailAlert { get; set; }
    public bool OverdueTextAlert { get; set; }
    public bool OverdueDashboardAlert { get; set; }
    public int OfflineTime { get; set; } = 4;      // minutes
    public int OverdueTime { get; set; } = 30;     // minutes
    public int CheckInterval { get; set; } = 120;  // seconds
    public bool NeedsReboot { get; set; }
    public bool ChocoInstalled { get; set; }
    public bool MaintenanceMode { get; set; }
    public bool BlockPolicyInheritance { get; set; }
    public string? TimeZone { get; set; }
    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;
    public int? PolicyId { get; set; }
    public Policy? Policy { get; set; }
}

public class AgentHistory
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public DateTime Time { get; set; }
    public string Type { get; set; } = "cmd_run";
    public string? Command { get; set; }
    public string? Username { get; set; }
    public string? ScriptResults { get; set; } // jsonb
    public bool SaveToAgentNote { get; set; }
}
