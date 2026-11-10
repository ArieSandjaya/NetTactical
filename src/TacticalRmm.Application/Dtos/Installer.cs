namespace TacticalRmm.Application.Dtos;

public class InstallerRequest
{
    public int ClientId { get; set; }
    public int SiteId { get; set; }
    /// <summary>workstation | server</summary>
    public string AgentType { get; set; } = "workstation";
    /// <summary>amd64 | 386</summary>
    public string Arch { get; set; } = "amd64";
    /// <summary>How long the installer token stays valid (hours): 1, 8, 24, 72, 168 or 720.</summary>
    public int ExpiresHours { get; set; } = 24;
    public bool Rdp { get; set; }
    public bool Ping { get; set; }
    public bool Power { get; set; }
    /// <summary>Install the MeshCentral agent too. Default false: the agent is installed with --nomesh.</summary>
    public bool InstallMesh { get; set; }
    /// <summary>Public URL agents use to reach this server. Defaults to the URL of the current request.</summary>
    public string? Api { get; set; }
}

public record InstallerDto(string ClientName, string SiteName, string AgentType, string Arch, string Api, string FileName, string DownloadUrl,
    DateTime ExpiresAt, string Token, string CommandPrompt, string PowerShell, IReadOnlyList<string> Warnings);
