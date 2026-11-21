using System.Text.Json;
using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Application.Logic;

/// <summary>Reading the Windows event log of an asset with Get-WinEvent. Pure logic: input checks, the script, reading what it prints.</summary>
public static class EventLogs
{
    public static readonly IReadOnlyList<string> Logs = ["System", "Application", "Security", "Setup"];
    public static readonly IReadOnlyList<string> Levels = ["all", "critical", "error", "warning", "information"];
    public const int MaxHours = 24 * 30, MaxEntries = 500, DefaultEntries = 100, MaxText = 100, MessageChars = 2000;

    public static string? Validate(string? log, string? level, int hours, int max, string? text, int? eventId)
    {
        if (!Logs.Contains(log ?? "", StringComparer.OrdinalIgnoreCase)) return $"log must be one of: {string.Join(", ", Logs)}";
        if (!Levels.Contains(level ?? "", StringComparer.OrdinalIgnoreCase)) return $"level must be one of: {string.Join(", ", Levels)}";
        if (hours is < 1 or > MaxHours) return $"hours must be between 1 and {MaxHours}.";
        if (max is < 1 or > MaxEntries) return $"max must be between 1 and {MaxEntries}.";
        if (text is not null && (text.Length > MaxText || text.Any(char.IsControl))) return $"The search text is longer than {MaxText} characters or has control characters.";
        if (eventId is < 0 or > 65535) return "The event id must be between 0 and 65535.";
        return null;
    }

    public static string CanonicalLog(string log) => Logs.First(l => l.Equals(log, StringComparison.OrdinalIgnoreCase));

    private static int LevelNumber(string level) => level.ToLowerInvariant() switch { "critical" => 1, "error" => 2, "warning" => 3, "information" => 4, _ => 0 };

    /// <summary>One more than max is read so the caller can tell that there was more.</summary>
    public static string Script(string log, string level, int hours, int max, string? text, int? eventId)
    {
        var filter = $"$f = @{{ LogName = {FileBrowser.Q(CanonicalLog(log))}; StartTime = (Get-Date).AddHours(-{hours}) }}\n";
        if (LevelNumber(level) > 0) filter += $"$f.Level = {LevelNumber(level)}\n";
        if (eventId is { } id) filter += $"$f.Id = {id}\n";
        var where = string.IsNullOrWhiteSpace(text) ? "" : $" | Where-Object {{ ([string]$_.Message) -like $pat }}";
        var pat = string.IsNullOrWhiteSpace(text) ? "" : $"$pat = '*' + [WildcardPattern]::Escape({FileBrowser.Q(text.Trim())}) + '*'\n";
        return $@"$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; try {{
{filter}{pat}try {{
  $ev = @(Get-WinEvent -FilterHashtable $f{where} | Select-Object -First {max + 1})
}} catch {{ if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') {{ $ev = @() }} else {{ throw }} }}
$r = @($ev | ForEach-Object {{ [pscustomobject]@{{ time = $_.TimeCreated.ToUniversalTime().ToString('o'); id = $_.Id; level = $_.LevelDisplayName; provider = $_.ProviderName; message = $(if ($null -eq $_.Message) {{ '' }} else {{ ([string]$_.Message).Substring(0, [Math]::Min(([string]$_.Message).Length, {MessageChars})) }}); record = $_.RecordId }} }})
'@@OK@@' + (ConvertTo-Json -InputObject $r -Compress -Depth 3)
}} catch {{ '@@ERR@@ ' + $_.Exception.Message }}";
    }

    public static EventLogDto? Parse(string log, int max, string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<EventLogEntryDto>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                string? S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var id = e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out var n) ? n : 0;
                long? rec = e.TryGetProperty("record", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out var rn) ? rn : null;
                list.Add(new EventLogEntryDto(S("time"), id, S("level"), S("provider"), S("message"), rec));
            }
            return new EventLogDto(log, list.Take(max).ToList(), list.Count > max);
        }
        catch (JsonException) { return null; }
    }
}
