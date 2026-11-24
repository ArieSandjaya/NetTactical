namespace TacticalRmm.Domain.Entities;

/// <summary>An application that can be installed silently on Windows assets (for example Adobe Acrobat Reader): where its installer is, and how to run it.</summary>
public class SoftwarePackage
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>"msi" or "exe".</summary>
    public string InstallerType { get; set; } = "msi";
    /// <summary>https:// address or \\server\share\file path of the installer.</summary>
    public string Source { get; set; } = "";
    public string? Sha256 { get; set; }
    /// <summary>Silent install options. An .msi defaults to "/qn /norestart"; an .exe must have them.</summary>
    public string? SilentArgs { get; set; }
    public int TimeoutSeconds { get; set; } = 900;
    /// <summary>When set, a computer that already lists an installed program whose name contains this text is skipped.</summary>
    public string? DetectName { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>One rollout of a package to a set of assets. Computers that are offline stay waiting until they come online or the rollout expires.</summary>
public class AppDeployment
{
    public int Id { get; set; }
    public int PackageId { get; set; }
    public SoftwarePackage Package { get; set; } = null!;
    /// <summary>Install again even when the program is already there.</summary>
    public bool Reinstall { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<AppDeploymentTarget> Targets { get; set; } = [];
}

public class AppDeploymentTarget
{
    public int Id { get; set; }
    public int DeploymentId { get; set; }
    public AppDeployment Deployment { get; set; } = null!;
    public int AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    /// <summary>pending, running, ok, failed, skipped, expired.</summary>
    public string Status { get; set; } = "pending";
    public string? Message { get; set; }
    public int Attempts { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
