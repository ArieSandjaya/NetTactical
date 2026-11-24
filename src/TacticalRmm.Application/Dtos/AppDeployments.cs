namespace TacticalRmm.Application.Dtos;

public class SoftwarePackageRequest
{
    public string Name { get; set; } = "";
    public string InstallerType { get; set; } = "msi";
    public string Source { get; set; } = "";
    public string? Sha256 { get; set; }
    public string? SilentArgs { get; set; }
    public int TimeoutSeconds { get; set; } = 900;
    public string? DetectName { get; set; }
    public string? Note { get; set; }
}

public record SoftwarePackageDto(int Id, string Name, string InstallerType, string Source, string? Sha256, string? SilentArgs, int TimeoutSeconds, string? DetectName, string? Note, string? CreatedBy, DateTime CreatedAt);

public class AppDeploymentRequest
{
    public int PackageId { get; set; }
    public bool Reinstall { get; set; }
    public int ExpiresDays { get; set; } = 7;
    public List<string>? AgentIds { get; set; }
    public List<int>? SiteIds { get; set; }
    public List<int>? ClientIds { get; set; }
}

public record AppTargetDto(string AgentId, string Hostname, string ClientName, string SiteName, string Status, string? Message, int Attempts, DateTime? UpdatedAt);

public record AppDeploymentDto(int Id, int PackageId, string PackageName, bool Reinstall, DateTime CreatedAt, DateTime ExpiresAt, bool Expired, string? CreatedBy,
    int Total, int Pending, int Running, int Ok, int Failed, int Skipped, int ExpiredCount, IReadOnlyList<AppTargetDto>? Targets);

/// <summary>One unit of work for the background runner: what to run on which asset.</summary>
public record AppWork(int TargetId, string AgentId, string Hostname, string Script, int TimeoutSeconds, string Username, string PackageName, int Attempts);
