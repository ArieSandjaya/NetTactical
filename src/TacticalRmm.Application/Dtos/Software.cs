using TacticalRmm.Application.Logic;

namespace TacticalRmm.Application.Dtos;

/// <summary>The installed applications of one asset. UpdatedAt is null when no list has been received yet.</summary>
public record SoftwareDto(IReadOnlyList<SoftwareItem> Items, DateTime? UpdatedAt);

/// <summary>Picks an application from the stored list. Name and version must match an entry exactly; the command is never taken from the request.</summary>
public record SoftwareUninstallRequest(string? Name, string? Version);

/// <summary>Output of the uninstall command. Silent = a silent form was used; otherwise the application's own uninstaller ran and may need someone at the computer.</summary>
public record SoftwareUninstallResult(string Output, bool Silent);
