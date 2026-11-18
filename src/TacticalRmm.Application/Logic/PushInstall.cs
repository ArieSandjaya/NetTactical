using System.Net;
using System.Text.RegularExpressions;

namespace TacticalRmm.Application.Logic;

/// <summary>Pure rules for pushing the agent to remote Windows computers with a domain administrator account (WinRM).</summary>
public static partial class PushInstall
{
    public const int MaxTargets = 20;

    public const string Installed = "installed", AlreadyInstalled = "already_installed", Failed = "failed", Skipped = "skipped", Queued = "queued", Running = "running";
    public const string CatOk = "ok", CatAuth = "auth", CatUnreachable = "unreachable", CatTimeout = "timeout", CatError = "error";

    [GeneratedRegex(@"^[A-Za-z0-9_]([A-Za-z0-9_-]{0,61}[A-Za-z0-9_])?(\.[A-Za-z0-9_]([A-Za-z0-9_-]{0,61}[A-Za-z0-9_])?)*$")]
    private static partial Regex HostRx();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,62}\\[A-Za-z0-9._$-]{1,64}$")]
    private static partial Regex DomainUserRx();
    [GeneratedRegex(@"^[A-Za-z0-9._%+-]{1,64}@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$")]
    private static partial Regex UpnRx();
    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex SplitRx();

    /// <summary>Splits a pasted list (one per line, or separated by commas/semicolons/spaces). Returns the distinct valid targets, or an error naming the first bad entry.</summary>
    public static (List<string> Hosts, string? Error) ParseTargets(string? text)
    {
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in SplitRx().Split(text ?? "").Select(CleanTarget).Where(x => x.Length > 0))
        {
            if (ValidateTarget(raw) is { } e) return ([], $"'{Trim(raw)}': {e}");
            if (seen.Add(raw)) hosts.Add(raw);
        }
        if (hosts.Count == 0) return ([], "enter at least one hostname or IPv4 address");
        if (hosts.Count > MaxTargets) return ([], $"at most {MaxTargets} computers per push");
        return (hosts, null);
    }

    /// <summary>Tolerates what people paste: \\\\PC01, "PC01", PC01. (trailing dot), or a name copied with quotes.</summary>
    private static string CleanTarget(string s) => s.Trim().Trim('"', '\'', '`').TrimStart('\\').TrimEnd('.');

    /// <summary>The name WinRM connects to: a name without a dot gets the DNS suffix (if one is set); IP addresses and full names are unchanged.</summary>
    public static string Connect(string host, string? dnsSuffix)
    {
        var sfx = (dnsSuffix ?? "").Trim().Trim('.');
        return sfx.Length == 0 || host.Contains('.') || IPAddress.TryParse(host, out _) || !HostRx().IsMatch(sfx) ? host : $"{host}.{sfx}";
    }

    /// <summary>Extra advice when a short name fails; null for IP addresses and full names.</summary>
    public static string? NameHint(string host, string? dnsSuffix) =>
        host.Contains('.') || IPAddress.TryParse(host, out _) ? null
        : string.IsNullOrWhiteSpace(dnsSuffix)
            ? $"'{host}' is a short name and the server (a Docker container) usually cannot resolve those. Type the full name ({host}.yourdomain.local) or the IP address, or set Tactical__Push__DnsSuffix to your domain."
            : $"The server tried {Connect(host, dnsSuffix)}. Check that this name resolves from the server, or use the IP address.";

    private static string Trim(string s) => s.Length <= 40 ? s : s[..40] + "…";

    /// <summary>Null when acceptable. Only a DNS host name or a plain IPv4 address; never loopback, link-local, multicast or unspecified.</summary>
    public static string? ValidateTarget(string host)
    {
        if (host.Length is 0 or > 253) return "not a valid hostname";
        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || host.Count(c => c == '.') != 3) return "only IPv4 addresses (a.b.c.d) are supported";
            var b = ip.GetAddressBytes();
            if (b[0] == 127 || b[0] == 0 || b[0] >= 224 || (b[0] == 169 && b[1] == 254)) return "this address cannot be used (loopback, link-local, multicast or unspecified)";
            return null;
        }
        if (!HostRx().IsMatch(host)) return "not a valid hostname or IPv4 address";
        var last = host[(host.LastIndexOf('.') + 1)..];
        if (last.All(char.IsDigit)) return "not a valid hostname or IPv4 address";            // 2130706433, 1.2.3 and similar tricks
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return "this computer cannot be a target";
        return null;
    }

    /// <summary>DOMAIN\user or user@domain.tld. A bare local account name is refused: the point is a domain administrator.</summary>
    public static string? ValidateUsername(string? u) =>
        string.IsNullOrWhiteSpace(u) ? "username is required"
        : DomainUserRx().IsMatch(u) || UpnRx().IsMatch(u) ? null
        : "use DOMAIN\\user or user@domain.tld";

    public static string? ValidatePassword(string? p) =>
        string.IsNullOrEmpty(p) ? "password is required" : p.Length > 256 ? "password is too long" : null;

    /// <summary>Script that runs ON the target (as the domain admin). Its only inputs are values already validated by InstallerCommands.</summary>
    public static string RemoteScript(string downloadUrl, string fileName, string installArgs, bool force)
    {
        if (!Regex.IsMatch(fileName, @"^tacticalagent-v[0-9.]+-windows-(amd64|386)\.exe$")) throw new ArgumentException("invalid file name");
        if (!downloadUrl.StartsWith("https://github.com/amidaware/rmmagent/releases/download/", StringComparison.Ordinal)) throw new ArgumentException("invalid download url");
        var dir = @"C:\Program Files\TacticalAgent";
        return
            "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'\n" +
            $"$force = ${(force ? "true" : "false")}\n" +
            "$me=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()\n" +
            "if(-not $me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){ throw 'The account is not a local administrator on this computer.' }\n" +
            $"$exe='{dir}\\tacticalrmm.exe'; $un='{dir}\\unins000.exe'\n" +
            "if(Test-Path $exe){\n" +
            "  if(-not $force){ return 'already_installed' }\n" +
            "  if(Test-Path $un){ Start-Process -FilePath $un -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES' -Wait; Start-Sleep -Seconds 5 }\n" +
            "}\n" +
            "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12\n" +
            $"$f=Join-Path $env:TEMP '{fileName}'\n" +
            "try {\n" +
            $"  Invoke-WebRequest -Uri '{downloadUrl}' -OutFile $f -UseBasicParsing\n" +
            "  Start-Process -FilePath $f -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES' -Wait; Start-Sleep -Seconds 5\n" +
            "  if(-not (Test-Path $exe)){ throw 'The setup ran but the agent files were not installed.' }\n" +
            $"  $p=Start-Process -FilePath $exe -ArgumentList '{installArgs.Replace("'", "''")}' -Wait -PassThru -NoNewWindow\n" +
            "  if($p.ExitCode -ne 0){ throw \"Agent registration exited with code $($p.ExitCode).\" }\n" +
            "} finally { Remove-Item $f -Force -ErrorAction SilentlyContinue }\n" +
            "'installed'\n";
    }

    /// <summary>Maps a raw PowerShell/WinRM error message to a category. Authentication failures stop the whole push to avoid locking the domain account.</summary>
    public static string Categorize(string? message)
    {
        var m = message ?? "";
        if (m.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || m.Contains("logon failure", StringComparison.OrdinalIgnoreCase)
            || m.Contains("user name or password", StringComparison.OrdinalIgnoreCase) || m.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || m.Contains("0x80070005", StringComparison.OrdinalIgnoreCase) || m.Contains("0x8009030c", StringComparison.OrdinalIgnoreCase)) return CatAuth;
        if (m.Contains("cannot connect", StringComparison.OrdinalIgnoreCase) || m.Contains("WinRM cannot process", StringComparison.OrdinalIgnoreCase)
            || m.Contains("No such host", StringComparison.OrdinalIgnoreCase) || m.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase)
            || m.Contains("timed out", StringComparison.OrdinalIgnoreCase) && m.Contains("WinRM", StringComparison.OrdinalIgnoreCase)) return CatUnreachable;
        return CatError;
    }

    /// <summary>Removes every secret from text that may be shown to a user or written to a log.</summary>
    public static string Scrub(string? text, params string?[] secrets)
    {
        var t = text ?? "";
        foreach (var s in secrets) if (!string.IsNullOrEmpty(s)) t = t.Replace(s, "***", StringComparison.Ordinal);
        return t.Length <= 600 ? t : t[..600] + "…";
    }
}
