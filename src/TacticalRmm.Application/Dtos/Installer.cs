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
    /// <summary>Public URL agents use to reach this server. Defaults to the URL of the current request.</summary>
    public string? Api { get; set; }
}

public record InstallerDto(string ClientName, string SiteName, string AgentType, string Arch, string Api, string FileName, string DownloadUrl,
    DateTime ExpiresAt, string Token, string CommandPrompt, string PowerShell, IReadOnlyList<string> Warnings);

public class PushInstallRequest
{
    public int ClientId { get; set; }
    public int SiteId { get; set; }
    public string AgentType { get; set; } = "workstation";
    public string Arch { get; set; } = "amd64";
    public bool Rdp { get; set; }
    public bool Ping { get; set; }
    public bool Power { get; set; }
    public string? Api { get; set; }
    /// <summary>Hostnames or IPv4 addresses, one per line (or comma separated).</summary>
    public string Targets { get; set; } = "";
    /// <summary>DOMAIN\user or user@domain.tld. Used for this push only; never stored or logged.</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Reinstall even when the agent is already present (runs the uninstaller first).</summary>
    public bool Force { get; set; }
}

public record PushTargetDto(string Host, string Status, string? Message, string? Category);
public record PushJobDto(string Id, string State, DateTime CreatedAt, DateTime? FinishedAt, string ClientName, string SiteName, string Username, bool Force, IReadOnlyList<PushTargetDto> Targets, IReadOnlyList<string> Warnings);

public sealed class RemoteRunRequest(string host, string username, string password, string script, bool useSsl, int timeoutSeconds)
{
    public string Host { get; } = host;
    public string Username { get; } = username;
    public string Password { get; } = password;
    public string Script { get; } = script;
    public bool UseSsl { get; } = useSsl;
    public int TimeoutSeconds { get; } = timeoutSeconds;
    public override string ToString() => $"RemoteRun({Host}, {Username}, ***)";   // never print the password
}
public record RemoteRunResult(string Status, string? Message, string Category);
