namespace TacticalRmm.Application.Logic;

/// <summary>Decides whether and how an agent is updated to the version set in Tactical:LatestAgentVer. Pure logic.</summary>
public static class AgentUpdatePlan
{
    /// <summary>Either a command payload (url, version, inno) or a Skip/Error text.</summary>
    public record Plan(IDictionary<string, object?>? Payload, string? Skip, string? Error);

    public static Plan Decide(string? plat, string? goarch, string? currentVersion, string latest)
    {
        if (!string.Equals(plat, "windows", StringComparison.OrdinalIgnoreCase)) return new(null, null, "Agent update from the dashboard is only available for Windows assets.");
        if (!AgentVersion.IsInstallerOutdated(currentVersion ?? "", latest) && !string.IsNullOrWhiteSpace(currentVersion)) return new(null, "Already on the latest version.", null);
        var arch = string.IsNullOrWhiteSpace(goarch) ? "amd64" : goarch.Trim().ToLowerInvariant();
        var url = InstallerCommands.DownloadUrl(latest, arch);
        if (url is null) return new(null, null, $"No download is known for version {latest} / {arch}. Check Tactical:LatestAgentVer.");
        return new(new Dictionary<string, object?>
        {
            ["func"] = "agentupdate",
            ["payload"] = new Dictionary<string, object?> { ["url"] = url, ["version"] = latest, ["inno"] = InstallerCommands.FileName(latest, arch) },
        }, null, null);
    }
}
