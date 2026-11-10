using System.Text.RegularExpressions;

namespace TacticalRmm.Application.Logic;

/// <summary>Builds the agent download URL and install commands. Pure: every value is validated before it is placed in a command line.</summary>
public static partial class InstallerCommands
{
    public static readonly IReadOnlyList<string> AgentTypes = ["workstation", "server"];
    public static readonly IReadOnlyList<string> Architectures = ["amd64", "386"];
    public static readonly IReadOnlyList<int> ValidHours = [1, 8, 24, 72, 168, 720];

    public const string InstallDir = @"C:\Program Files\TacticalAgent\tacticalrmm.exe";

    [GeneratedRegex(@"^https?://[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?(:\d{1,5})?$")]
    private static partial Regex ApiRx();
    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex VerRx();
    [GeneratedRegex(@"^[0-9a-f]{32,128}$")]
    private static partial Regex TokenRx();

    /// <summary>Returns null when valid. Only scheme://host[:port] is accepted (no path, query, quotes or spaces).</summary>
    public static string? ValidateApi(string? api) =>
        string.IsNullOrWhiteSpace(api) ? "api url is required"
        : !ApiRx().IsMatch(api.Trim()) ? "api url must look like https://rmm.example.com (scheme, host and optional port only)"
        : null;

    public static string NormalizeApi(string api) => api.Trim().TrimEnd('/');

    public static string FileName(string version, string arch) => $"tacticalagent-v{version}-windows-{arch}.exe";

    /// <summary>Official release of the unchanged Tactical RMM Go agent.</summary>
    public static string? DownloadUrl(string version, string arch) =>
        VerRx().IsMatch(version) && Architectures.Contains(arch)
            ? $"https://github.com/amidaware/rmmagent/releases/download/v{version}/{FileName(version, arch)}" : null;

    public static string InstallArguments(string api, int clientId, int siteId, string agentType, string token, bool rdp, bool ping, bool power)
    {
        if (ValidateApi(api) is { } e) throw new ArgumentException(e);
        if (!AgentTypes.Contains(agentType)) throw new ArgumentException("agent type must be workstation or server");
        if (!TokenRx().IsMatch(token)) throw new ArgumentException("invalid token");
        var parts = new List<string> { "-m", "install", "--api", $"\"{NormalizeApi(api)}\"", "--client-id", clientId.ToString(), "--site-id", siteId.ToString(),
            "--agent-type", $"\"{agentType}\"", "--auth", $"\"{token}\"" };
        if (rdp) parts.Add("--rdp");
        if (ping) parts.Add("--ping");
        if (power) parts.Add("--power");
        return string.Join(' ', parts);
    }

    /// <summary>Command Prompt (as Administrator), run in the folder that contains the downloaded file. Same sequence as upstream's "manual" install.</summary>
    public static string CommandPrompt(string fileName, string installArgs) =>
        $"\"{fileName}\" /VERYSILENT /SUPPRESSMSGBOXES && ping 127.0.0.1 -n 5 && \"{InstallDir}\" {installArgs}";

    /// <summary>PowerShell (as Administrator): downloads the agent, runs the silent setup, then registers it.</summary>
    public static string PowerShell(string downloadUrl, string fileName, string installArgs) =>
        "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " +
        $"$f=Join-Path $env:TEMP '{fileName}'; Invoke-WebRequest -Uri '{downloadUrl}' -OutFile $f; " +
        "Start-Process -FilePath $f -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES' -Wait; Start-Sleep -Seconds 5; " +
        $"Start-Process -FilePath '{InstallDir}' -ArgumentList '{installArgs.Replace("'", "''")}' -Wait -NoNewWindow";
}
