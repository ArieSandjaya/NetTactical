using System.Collections;
using System.Text.Json;

namespace TacticalRmm.Application.Logic;

/// <summary>One installed application as reported by an agent.</summary>
public sealed record SoftwareItem(string Name, string? Version, string? Publisher, string? InstallDate, string? Size, string? Source, string? Uninstall = null);

/// <summary>
/// The installed-software list of an agent: reads what the agent sends (NATS reply or the JSON it posts), keeps only the fields the dashboard uses,
/// bounds the size, and filters by name. Pure logic; the input comes from an endpoint and is treated as untrusted.
/// </summary>
public static class SoftwareList
{
    public const int MaxItems = 10_000, MaxText = 300, MaxUninstall = 1000;

    private static readonly string[] Keys = ["name", "version", "publisher", "install_date", "size", "source", "uninstall"];

    private static string? Clean(object? v, int max = MaxText)
    {
        var t = v switch { null => null, string s => s, bool or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture), _ => null };
        t = t?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > max ? t[..max] : t;
    }

    private static List<SoftwareItem> Build(IEnumerable<Func<string, object?>> rows)
    {
        var list = new List<SoftwareItem>();
        foreach (var get in rows)
        {
            var name = Clean(get("name"));
            if (name is null) continue;
            list.Add(new SoftwareItem(name, Clean(get("version")), Clean(get("publisher")), Clean(get("install_date")), Clean(get("size")), Clean(get("source")), Clean(get("uninstall"), MaxUninstall)));
            if (list.Count >= MaxItems) break;
        }
        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Version, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The decoded NATS reply (a list of maps), or null when it is not a list.</summary>
    public static List<SoftwareItem>? FromReply(object? reply)
    {
        if (reply is not IEnumerable seq || reply is string || reply is IDictionary) return null;   // a single map is not a list
        var rows = new List<Func<string, object?>>();
        foreach (var entry in seq)
        {
            if (entry is not IDictionary map) continue;
            rows.Add(key =>
            {
                foreach (DictionaryEntry kv in map) if (string.Equals(kv.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                return null;
            });
        }
        return Build(rows);
    }

    /// <summary>The stored or posted JSON (an array of objects). Anything else gives an empty list.</summary>
    public static List<SoftwareItem> ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var rows = new List<Func<string, object?>>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var copy = el.Clone();
                rows.Add(key =>
                {
                    foreach (var p in copy.EnumerateObject())
                        if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                            return p.Value.ValueKind switch { JsonValueKind.String => p.Value.GetString(), JsonValueKind.Number => p.Value.GetRawText(), _ => null };
                    return null;
                });
            }
            return Build(rows);
        }
        catch (JsonException) { return []; }
    }

    /// <summary>The form kept in the database: only the known fields, snake_case, no empty values.</summary>
    public static string ToJson(IEnumerable<SoftwareItem> items) => JsonSerializer.Serialize(items.Select(i =>
    {
        var d = new Dictionary<string, string>();
        void Put(string k, string? v) { if (v is not null) d[k] = v; }
        Put(Keys[0], i.Name); Put(Keys[1], i.Version); Put(Keys[2], i.Publisher); Put(Keys[3], i.InstallDate); Put(Keys[4], i.Size); Put(Keys[5], i.Source); Put(Keys[6], i.Uninstall);
        return d;
    }));

    /// <summary>Items whose name, publisher or version contains the text (ignoring case). Empty text keeps everything.</summary>
    public static IEnumerable<SoftwareItem> Filter(IEnumerable<SoftwareItem> items, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return items;
        var t = q.Trim();
        return items.Where(i => i.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
            || (i.Publisher?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
            || (i.Version?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false));
    }
}
