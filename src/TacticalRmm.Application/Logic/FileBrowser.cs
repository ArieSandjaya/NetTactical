using System.Text;
using System.Text.Json;
using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Application.Logic;

/// <summary>File browsing on a Windows asset. The agent has no file commands, so each step is a small PowerShell script run on it (as the agent's account,
/// normally SYSTEM). This class checks input, builds those scripts and reads what they print. Pure logic.</summary>
public static class FileBrowser
{
    public const int MaxDownloadBytes = 8 * 1024 * 1024, MaxUploadBytes = 8 * 1024 * 1024, MaxEntries = 3000, MaxPath = 1024;
    public const string Ok = "@@OK@@", Err = "@@ERR@@";

    /// <summary>"" (the drive list), a drive path (C:\ or C:\Users) or a UNC path (\\server\share). No wildcards or control characters are accepted.</summary>
    public static bool ValidPath(string? path, bool allowEmpty = false)
    {
        if (string.IsNullOrEmpty(path)) return allowEmpty;
        if (path.Length > MaxPath || path.Any(c => char.IsControl(c) || c is '*' or '?' or '"' or '<' or '>' or '|')) return false;
        var drive = path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path.Length == 2 || path[2] is '\\' or '/');
        return drive || path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 2;
    }

    /// <summary>A single file or folder name: no separators, no reserved characters, not . or .. and no trailing dot or space.</summary>
    public static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 255 && name is not ("." or "..") && !name.EndsWith('.') && !name.EndsWith(' ') &&
        !name.Any(c => char.IsControl(c) || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|');

    /// <summary>A drive root (C:\) or a bare share root is never deleted from here.</summary>
    public static bool IsRoot(string path)
    {
        var p = path.Replace('/', '\\').TrimEnd('\\');
        if (p.Length == 2 && p[1] == ':') return true;
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return p.Count(c => c == '\\') <= 3;   // \\server or \\server\share
        return false;
    }

    public static string? Parent(string path)
    {
        var p = path.Replace('/', '\\').TrimEnd('\\');
        if (IsRoot(p)) return path.StartsWith(@"\\") ? null : "";
        var i = p.LastIndexOf('\\');
        return i < 0 ? "" : i == 2 && p[1] == ':' ? p[..3] : p[..i];
    }

    public static string Combine(string dir, string name) => dir.TrimEnd('\\', '/') + "\\" + name;

    /// <summary>A PowerShell single-quoted literal: only the quote itself needs doubling.</summary>
    public static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    private static string Ps(bool b) => b ? "$true" : "$false";

    private const string Head = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; try {\n";
    private const string Tail = "\n} catch { '@@ERR@@ ' + $_.Exception.Message }";

    public static string ListScript(string path) => Head + $@"
$p = {Q(path)}
if ($p -eq '') {{ $r = @(Get-PSDrive -PSProvider FileSystem | ForEach-Object {{ [pscustomobject]@{{ name = $_.Root; dir = $true; size = $null; modified = $null; free = $_.Free; used = $_.Used }} }}) }}
else {{ $r = @(Get-ChildItem -LiteralPath $p -Force | Select-Object -First {MaxEntries + 1} | ForEach-Object {{ [pscustomobject]@{{ name = $_.Name; dir = [bool]$_.PSIsContainer; size = $(if ($_.PSIsContainer) {{ $null }} else {{ $_.Length }}); modified = $_.LastWriteTimeUtc.ToString('o') }} }}) }}
'@@OK@@' + (ConvertTo-Json -InputObject $r -Compress -Depth 3)" + Tail;

    public static string DownloadScript(string path) => Head + $@"
$f = Get-Item -LiteralPath {Q(path)} -Force
if ($f.PSIsContainer) {{ throw 'This is a folder, not a file.' }}
if ($f.Length -gt {MaxDownloadBytes}) {{ throw 'The file is larger than {MaxDownloadBytes / 1024 / 1024} MB.' }}
'@@OK@@' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))" + Tail;

    public static string UploadScript(string dir, string name, string base64, bool overwrite) => Head + $@"
$d = Get-Item -LiteralPath {Q(dir)} -Force
if (-not $d.PSIsContainer) {{ throw 'The target is not a folder.' }}
$t = Join-Path $d.FullName {Q(name)}
if ((Test-Path -LiteralPath $t) -and -not {Ps(overwrite)}) {{ throw 'A file with this name already exists.' }}
[IO.File]::WriteAllBytes($t, [Convert]::FromBase64String({Q(base64)}))
'@@OK@@' + (Get-Item -LiteralPath $t).Length" + Tail;

    public static string MkdirScript(string dir, string name) => Head + $@"
$t = Join-Path {Q(dir)} {Q(name)}
if (Test-Path -LiteralPath $t) {{ throw 'This name already exists.' }}
New-Item -ItemType Directory -Path $t | Out-Null
'@@OK@@'" + Tail;

    public static string DeleteScript(string path, bool recursive) => Head + $@"
$f = Get-Item -LiteralPath {Q(path)} -Force
if ($f.PSIsContainer -and -not {Ps(recursive)} -and @(Get-ChildItem -LiteralPath $f.FullName -Force).Count -gt 0) {{ throw 'The folder is not empty.' }}
Remove-Item -LiteralPath $f.FullName -Recurse -Force
'@@OK@@'" + Tail;

    public static string RenameScript(string path, string newName) => Head + $@"
Rename-Item -LiteralPath {Q(path)} -NewName {Q(newName)}
'@@OK@@'" + Tail;

    /// <summary>The text the script printed, from a runscript reply (a map with stdout/stderr/retcode) or a plain string. Error is set for a transport
    /// problem (timeout, NATS down) or an agent that could not run it.</summary>
    public static (string? Text, string? Error) Output(object? reply)
    {
        switch (reply)
        {
            case "timeout": return (null, "The agent did not answer (it may be offline).");
            case "natsdown": return (null, "The NATS server is not reachable, so the agent cannot be asked.");
            case "malformed": return (null, "The agent sent a reply that could not be read.");
            case string s: return (s, null);
            case System.Collections.IDictionary m:
            {
                string? Get(string k) { foreach (System.Collections.DictionaryEntry e in m) if (string.Equals(e.Key?.ToString(), k, StringComparison.OrdinalIgnoreCase)) return e.Value?.ToString(); return null; }
                var stdout = Get("stdout") ?? "";
                if (stdout.Contains(Ok, StringComparison.Ordinal) || stdout.Contains(Err, StringComparison.Ordinal)) return (stdout, null);
                var stderr = Get("stderr");
                return (null, string.IsNullOrWhiteSpace(stderr) ? "The agent ran the script but it printed no result." : Short(stderr));
            }
            default: return (null, "Unexpected reply from the agent.");
        }
    }

    private static string Short(string s) { s = s.Trim(); return s.Length > 300 ? s[..300] : s; }

    /// <summary>Splits the script's output into its payload or an error message. The marker may follow other lines (progress, warnings).</summary>
    public static (string? Payload, string? Error) Payload(string? text)
    {
        if (text is null) return (null, "The agent sent no output.");
        var e = text.IndexOf(Err, StringComparison.Ordinal);
        if (e >= 0) return (null, Short(text[(e + Err.Length)..]));
        var o = text.IndexOf(Ok, StringComparison.Ordinal);
        return o < 0 ? (null, "The agent sent an unexpected reply.") : (text[(o + Ok.Length)..].Trim(), null);
    }

    public static FileListDto? ParseList(string path, string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<FileEntryDto>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                long? L(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : null;
                var dir = e.TryGetProperty("dir", out var d) && d.ValueKind == JsonValueKind.True;
                var mod = e.TryGetProperty("modified", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                list.Add(new FileEntryDto(n.GetString()!, dir, dir ? null : L("size"), mod, L("free"), L("used")));
            }
            var truncated = list.Count > MaxEntries;
            var rows = list.Take(MaxEntries).OrderByDescending(x => x.IsDir && path.Length > 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return new FileListDto(path, path.Length == 0 ? null : Parent(path), rows, truncated);
        }
        catch (JsonException) { return null; }
    }

    public static byte[]? ParseBase64(string payload)
    {
        try { return Convert.FromBase64String(payload); } catch (FormatException) { return null; }
    }

    public static string DownloadName(string path) => path.Replace('/', '\\').TrimEnd('\\').Split('\\').Last();
}
