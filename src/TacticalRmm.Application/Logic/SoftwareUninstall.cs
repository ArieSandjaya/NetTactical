using System.Text.RegularExpressions;

namespace TacticalRmm.Application.Logic;

/// <summary>What to run on the agent to remove an application. Silent = the command is a known silent form; otherwise the application's own uninstaller runs as registered and may show a window.</summary>
public sealed record UninstallPlan(string Command, bool Silent);

/// <summary>
/// Turns the uninstall string that Windows keeps for an application into a command. Windows Installer entries (<c>MsiExec.exe /I{GUID}</c> or <c>/X{GUID}</c>)
/// become a silent <c>msiexec /x {GUID} /qn /norestart</c>; anything else runs exactly as registered. The text comes from the agent that will run it,
/// never from the browser, so a request can only pick an application from the stored list.
/// </summary>
public static class SoftwareUninstall
{
    private static readonly Regex Msi = new(@"^\s*""?(?:[A-Za-z]:\\Windows\\System32\\)?msiexec(?:\.exe)?""?\s+/[ix]\s*(\{[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public static UninstallPlan? Plan(string? uninstallString)
    {
        var s = uninstallString?.Trim();
        if (string.IsNullOrEmpty(s) || s.Length > SoftwareList.MaxUninstall) return null;
        if (s.Any(c => c < ' ')) return null;   // no line breaks or control characters in a command
        var m = Msi.Match(s);
        return m.Success ? new UninstallPlan($"msiexec.exe /x {m.Groups[1].Value} /qn /norestart", true) : new UninstallPlan(s, false);
    }
}
