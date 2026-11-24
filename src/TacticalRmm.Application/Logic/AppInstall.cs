using System.Text.RegularExpressions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;

namespace TacticalRmm.Application.Logic;

/// <summary>Rules for installing applications (.msi or .exe) on assets as a rollout: input checks, the PowerShell run on the asset, and reading its answer. Pure logic.</summary>
public static partial class AppInstall
{
    public const string Msi = "msi", Exe = "exe";
    public const string Pending = "pending", Running = "running", Ok = "ok", Failed = "failed", Skipped = "skipped", Expired = "expired";
    public const int MaxTargets = 500, MaxName = 100, MaxNote = 200, MaxAttempts = 10, MinTimeout = 60, MaxTimeout = 3600;
    public static readonly IReadOnlyList<int> ValidDays = [1, 3, 7, 14, 30];
    public const string AlreadyInstalled = "Already installed";

    [GeneratedRegex(@"^[A-Za-z0-9 ._()+\-]{1,100}$")] private static partial Regex Detect();

    public static string? CheckSource(string? type, string? source)
    {
        if (type == Msi) return MsiInstall.CheckSource(source);
        if (string.IsNullOrWhiteSpace(source)) return "Enter the address of the installer file.";
        var s = source.Trim();
        if (s.Length > 1000 || s.Any(c => char.IsControl(c) || c is ' ' or '"' or '\'' or '`' or '$' or ';' or '|' or '<' or '>')) return "The address contains characters that are not allowed.";
        if (s.StartsWith(@"\\", StringComparison.Ordinal))
            return s.Length > 2 && s.Count(c => c == '\\') >= 4 && s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? null : @"A network path must look like \\server\share\file.exe.";
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return "Use an https:// address or a network path (\\\\server\\share\\file.exe).";
        return u.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? null : "The address must end in .exe.";
    }

    public static string? Validate(SoftwarePackageRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > MaxName || r.Name.Any(char.IsControl)) return $"Enter a name of up to {MaxName} characters.";
        if (r.InstallerType is not (Msi or Exe)) return "The installer type must be msi or exe.";
        if (CheckSource(r.InstallerType, r.Source) is { } se) return se;
        if (MsiInstall.CheckSha(r.Sha256) is { } he) return he;
        if (MsiInstall.CheckArgs(r.SilentArgs) is { } ae) return ae;
        if (r.InstallerType == Exe && string.IsNullOrWhiteSpace(r.SilentArgs)) return "Enter the silent install options of the .exe (for example /S or /quiet). Without them the installer would wait for someone to click.";
        if (r.TimeoutSeconds < MinTimeout || r.TimeoutSeconds > MaxTimeout) return $"The time limit must be between {MinTimeout} and {MaxTimeout} seconds.";
        if (!string.IsNullOrWhiteSpace(r.DetectName) && !Detect().IsMatch(r.DetectName.Trim())) return "The program name used to detect an existing install has characters that are not allowed.";
        if (r.Note is { Length: > MaxNote } || r.Note?.Any(char.IsControl) == true) return $"The note is longer than {MaxNote} characters or has control characters.";
        return null;
    }

    public static string? Validate(AppDeploymentRequest r)
    {
        if (r.PackageId <= 0) return "Choose a package.";
        if (!ValidDays.Contains(r.ExpiresDays)) return $"expires_days must be one of {string.Join(", ", ValidDays)}";
        var n = (r.AgentIds?.Count ?? 0) + (r.SiteIds?.Count ?? 0) + (r.ClientIds?.Count ?? 0);
        if (n == 0) return "Choose at least one client, site or asset.";
        if (r.AgentIds is { } ids && (ids.Any(i => !AgentIdValidator.IsValid(i)) || ids.Distinct().Count() != ids.Count)) return "An asset id is not valid or is listed twice.";
        return null;
    }

    /// <summary>The PowerShell run on the asset. Prints @@OK@@ text or @@ERR@@ text (see FileBrowser.Payload).</summary>
    public static string Script(string type, string source, string? sha, string? args, string? detectName, bool reinstall)
    {
        var isMsi = type == Msi;
        var extra = isMsi ? (string.IsNullOrWhiteSpace(args) ? MsiInstall.DefaultArgs : args.Trim()) + (reinstall ? " REINSTALL=ALL REINSTALLMODE=vomus" : "") : (args ?? "").Trim();
        var detect = reinstall ? "" : (detectName ?? "").Trim();
        return $@"$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8
$src = {FileBrowser.Q(source.Trim())}; $sha = {FileBrowser.Q((sha ?? "").Trim())}; $extra = {FileBrowser.Q(extra)}; $detect = {FileBrowser.Q(detect)}; $isMsi = ${(isMsi ? "true" : "false")}
$tmp = Join-Path $env:TEMP ('ntapp_' + [guid]::NewGuid().ToString('N') + $(if ($isMsi) {{ '.msi' }} else {{ '.exe' }})); $log = $tmp + '.log'
try {{
  $skip = $null
  if ($detect -ne '') {{
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    $hit = Get-ItemProperty -Path $keys -ErrorAction SilentlyContinue | Where-Object {{ $_.DisplayName -and $_.DisplayName -like ('*' + $detect + '*') }} | Select-Object -First 1
    if ($hit) {{ $skip = '{AlreadyInstalled}: ' + $hit.DisplayName + ' ' + $hit.DisplayVersion + '. Skipped.' }}
  }}
  if ($skip) {{ '@@OK@@' + $skip }}
  else {{
    if ($src.StartsWith('https://')) {{ [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri $src -OutFile $tmp -UseBasicParsing }}
    else {{ Copy-Item -LiteralPath $src -Destination $tmp }}
    if ($sha -ne '' -and (Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash -ne $sha) {{ throw 'The SHA-256 of the downloaded file does not match; nothing was installed.' }}
    if ($isMsi) {{ $p = Start-Process -FilePath msiexec.exe -ArgumentList ('/i ""' + $tmp + '"" ' + $extra + ' /L*v ""' + $log + '""') -Wait -PassThru }}
    else {{ $p = Start-Process -FilePath $tmp -ArgumentList $extra -Wait -PassThru }}
    $c = $p.ExitCode
    $why = @{{ 1602 = 'cancelled'; 1603 = 'fatal error during installation'; 1618 = 'another installation is already in progress'; 1619 = 'the package could not be opened'; 1620 = 'the package is not a valid installer'; 1625 = 'blocked by a policy on the computer'; 1638 = 'another version of this product is already installed (use reinstall)'; 1639 = 'invalid install options' }}
    if ($c -eq 0) {{ '@@OK@@Installed (exit code 0).' }}
    elseif ($c -eq 3010) {{ '@@OK@@Installed; a restart is needed to finish (exit code 3010).' }}
    elseif ($c -eq 1641) {{ '@@OK@@Installed; the computer is restarting (exit code 1641).' }}
    else {{ throw ('Installer exit code ' + $c + $(if ($why.ContainsKey($c)) {{ ': ' + $why[$c] }} else {{ '' }})) }}
  }}
}} catch {{ '@@ERR@@ ' + $_.Exception.Message }}
finally {{ Remove-Item -LiteralPath $tmp, $log -Force -ErrorAction SilentlyContinue }}";
    }

    /// <summary>Turns what the asset answered into the target's next status. An asset that did not answer stays waiting (until it has been tried too often).</summary>
    public static (string Status, string Message) Interpret(string? payload, string? error, int attempts)
    {
        if (error is not null)
        {
            var text = error.Length > 500 ? error[..500] : error;
            if (error.Contains("did not answer", StringComparison.OrdinalIgnoreCase))
                return attempts >= MaxAttempts ? (Failed, $"The computer did not answer after {attempts} tries.") : (Pending, "Waiting: the computer did not answer. It will be tried again when it is online.");
            return (Failed, text);
        }
        var p = (payload ?? "").Trim();
        if (p.Length > 500) p = p[..500];
        return p.StartsWith(AlreadyInstalled, StringComparison.Ordinal) ? (Skipped, p) : (Ok, p.Length == 0 ? "Installed." : p);
    }
}
