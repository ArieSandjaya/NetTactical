using System.Text.Json;

namespace TacticalRmm.Application.Logic;

/// <summary>Notes and run history of an asset: input cleaning and reading the stored script result. Pure logic.</summary>
public static class AgentLogRules
{
    public const int MaxNote = 5000, MaxCommand = 1000, MaxOutput = 4000, MaxHistory = 200, MaxNotes = 500;

    /// <summary>The trimmed note, or null when it is empty.</summary>
    public static string? CleanNote(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > MaxNote ? t[..MaxNote] : t;
    }

    public static string CommandText(string? command)
    {
        var t = (command ?? "").Trim();
        return t.Length > MaxCommand ? t[..MaxCommand] + "…" : t;
    }

    public static int Take(int? take) => Math.Clamp(take ?? 50, 1, MaxHistory);

    private static string? Cut(string? s) { var t = s?.Trim(); return string.IsNullOrEmpty(t) ? null : t.Length > MaxOutput ? t[..MaxOutput] + "…" : t; }

    /// <summary>Reads the stored result of a script or task run ({stdout, stderr, retcode, execution_time}); anything unreadable gives nulls.</summary>
    public static (string? Stdout, string? Stderr, int? Retcode, double? ExecutionTime) ParseResults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return default;
            string? Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int? ret = r.TryGetProperty("retcode", out var rc) && rc.ValueKind == JsonValueKind.Number && rc.TryGetInt32(out var i) ? i : null;
            double? ex = r.TryGetProperty("execution_time", out var et) && et.ValueKind == JsonValueKind.Number && et.TryGetDouble(out var d) ? d : null;
            return (Cut(Str("stdout")), Cut(Str("stderr")), ret, ex);
        }
        catch (JsonException) { return default; }
    }
}
