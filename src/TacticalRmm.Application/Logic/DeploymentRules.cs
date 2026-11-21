using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Application.Logic;

/// <summary>Rules for deployment links. Pure logic.</summary>
public static partial class DeploymentRules
{
    public static readonly IReadOnlyList<int> ValidHours = [1, 24, 72, 168, 720, 2160, 8760];
    public const int MaxNote = 200, MaxUsesLimit = 100000;

    [GeneratedRegex("^[0-9a-f]{32}$")] private static partial Regex UidRx();

    public static string NewUid() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    public static bool ValidUid(string? uid) => uid is not null && UidRx().IsMatch(uid);

    public static string? Validate(DeploymentRequest r, string api)
    {
        if (!InstallerCommands.AgentTypes.Contains(r.AgentType)) return "agent_type must be workstation or server";
        if (!InstallerCommands.Architectures.Contains(r.Arch)) return "arch must be amd64 or 386";
        if (InstallerCommands.NatsPortError(r.NatsPort) is { } npe) return npe;
        if (!ValidHours.Contains(r.ExpiresHours)) return $"expires_hours must be one of {string.Join(", ", ValidHours)}";
        if (InstallerCommands.ValidateApi(api) is { } ae) return ae;
        if (r.MaxUses is < 1 or > MaxUsesLimit) return $"max_uses must be between 1 and {MaxUsesLimit} (or empty for no limit)";
        if (r.Note is { Length: > MaxNote } || r.Note?.Any(char.IsControl) == true) return $"The note is longer than {MaxNote} characters or has control characters.";
        return null;
    }

    public static string Link(string api, string uid) => $"{InstallerCommands.NormalizeApi(api)}/deploy/{uid}";

    /// <summary>One line for a computer that has no way to paste a long script: run in PowerShell as Administrator.</summary>
    public static string Command(string link) => $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"irm '{link}' | iex\"";

    public static bool Expired(DateTime expiresAt, DateTime now) => expiresAt <= now;

    /// <summary>The PowerShell served at the link. It refuses to run without administrator rights and when the agent is already installed.</summary>
    public static string Script(string clientName, string siteName, string downloadUrl, string fileName, string installArgs)
    {
        var label = (clientName + " / " + siteName).Replace("\r", " ").Replace("\n", " ");
        return $"# NetTactical agent deployment: {label}\n" +
               "if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Write-Error 'Run this in PowerShell as Administrator.'; return }\n" +
               $"if (Test-Path '{InstallerCommands.InstallDir}') {{ Write-Host 'The agent is already installed on this computer.'; return }}\n" +
               InstallerCommands.PowerShell(downloadUrl, fileName, installArgs) + "\n" +
               "Write-Host 'Done. The computer appears in the dashboard within a minute.'\n";
    }
}
