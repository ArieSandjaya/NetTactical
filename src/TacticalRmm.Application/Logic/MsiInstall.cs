using System.Text.RegularExpressions;

namespace TacticalRmm.Application.Logic;

/// <summary>Silent install of an .msi on a Windows asset: the agent downloads it (https) or copies it (network share), checks the SHA-256 if one is given,
/// runs msiexec and reports the exit code. "Reinstall" repairs/overwrites an installed copy. Pure logic: input checks, the script, reading the answer.</summary>
public static partial class MsiInstall
{
    public const int MaxTimeout = 3600, DefaultTimeout = 900;
    public const string DefaultArgs = "/qn /norestart";

    [GeneratedRegex("^[A-Fa-f0-9]{64}$")] private static partial Regex Sha();
    // msiexec switches and PROPERTY=value pairs. No quotes other than ", no ; & | < > ` $ ' or line breaks.
    [GeneratedRegex(@"^[A-Za-z0-9 /_=.,:\\\-""()]{0,500}$")] private static partial Regex Args();

    /// <summary>https://host/path/app.msi (query allowed) or \\server\share\app.msi. Returns null when fine, else the reason.</summary>
    public static string? CheckSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Enter the address of the .msi file.";
        var s = source.Trim();
        if (s.Length > 1000 || s.Any(c => char.IsControl(c) || c is ' ' or '"' or '\'' or '`' or '$' or ';' or '|' or '<' or '>')) return "The address contains characters that are not allowed.";
        if (s.StartsWith(@"\\", StringComparison.Ordinal))
            return s.Length > 2 && s.Count(c => c == '\\') >= 4 && s.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? null : @"A network path must look like \\server\share\file.msi.";
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return "Use an https:// address or a network path (\\\\server\\share\\file.msi).";
        return u.AbsolutePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? null : "The address must end in .msi.";
    }

    public static string? CheckArgs(string? args) => string.IsNullOrEmpty(args) || Args().IsMatch(args) ? null : "The install options contain characters that are not allowed.";
    public static string? CheckSha(string? sha) => string.IsNullOrWhiteSpace(sha) || Sha().IsMatch(sha.Trim()) ? null : "The SHA-256 must be 64 hexadecimal characters.";

    public static string? Validate(string? source, string? sha, string? args, int timeout) =>
        CheckSource(source) ?? CheckSha(sha) ?? CheckArgs(args) ?? (timeout is < 60 or > MaxTimeout ? $"The time limit must be between 60 and {MaxTimeout} seconds." : null);

    /// <summary>The PowerShell run on the agent. Prints @@OK@@ text or @@ERR@@ text (see FileBrowser.Payload).</summary>
    public static string Script(string source, string? sha, string? args, bool reinstall)
    {
        var extra = (string.IsNullOrWhiteSpace(args) ? DefaultArgs : args.Trim()) + (reinstall ? " REINSTALL=ALL REINSTALLMODE=vomus" : "");
        return $@"$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8
$src = {FileBrowser.Q(source.Trim())}; $sha = {FileBrowser.Q((sha ?? "").Trim())}; $extra = {FileBrowser.Q(extra)}
$tmp = Join-Path $env:TEMP ('ntmsi_' + [guid]::NewGuid().ToString('N') + '.msi'); $log = $tmp + '.log'
try {{
  if ($src.StartsWith('https://')) {{ [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri $src -OutFile $tmp -UseBasicParsing }}
  else {{ Copy-Item -LiteralPath $src -Destination $tmp }}
  if ($sha -ne '' -and (Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash -ne $sha) {{ throw 'The SHA-256 of the downloaded file does not match; nothing was installed.' }}
  $p = Start-Process -FilePath msiexec.exe -ArgumentList ('/i ""' + $tmp + '"" ' + $extra + ' /L*v ""' + $log + '""') -Wait -PassThru
  $c = $p.ExitCode
  $why = @{{ 1603 = 'fatal error during installation'; 1618 = 'another installation is already in progress'; 1619 = 'the package could not be opened'; 1620 = 'the package is not a valid .msi'; 1625 = 'blocked by a policy on the computer'; 1638 = 'another version of this product is already installed (use reinstall)'; 1639 = 'invalid install options'; 1641 = 'done, a restart was started' }}
  if ($c -eq 0) {{ '@@OK@@Installed (exit code 0).' }}
  elseif ($c -eq 3010) {{ '@@OK@@Installed; a restart is needed to finish (exit code 3010).' }}
  elseif ($c -eq 1641) {{ '@@OK@@Installed; the computer is restarting (exit code 1641).' }}
  else {{ throw ('msiexec exit code ' + $c + $(if ($why.ContainsKey($c)) {{ ': ' + $why[$c] }} else {{ '' }})) }}
}} catch {{ '@@ERR@@ ' + $_.Exception.Message }}
finally {{ Remove-Item -LiteralPath $tmp, $log -Force -ErrorAction SilentlyContinue }}";
    }
}
