using System.Globalization;
using System.Text.Json;
using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Application.Logic;

/// <summary>Parser defensif untuk jsonb yang ditulis nats-api (disk, layanan). Data buruk tidak pernah melempar.</summary>
public static class AgentDataParser
{
    public static IReadOnlyList<DiskDto> ParseDisks(string? json)
    {
        var list = new List<DiskDto>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var pct = Num(e, "percent");
                if (pct is null) continue;
                list.Add(new DiskDto(Str(e, "device") ?? "?", Str(e, "fstype"), Str(e, "total"), Str(e, "used"), Str(e, "free"),
                    Math.Round(Math.Clamp(pct.Value, 0, 100), 1)));
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static double? WorstDiskUsed(IReadOnlyList<DiskDto> disks) => disks.Count == 0 ? null : disks.Max(d => d.Percent);

    /// <summary>Layanan berstart otomatis yang tidak berjalan (maks <paramref name="limit"/>).</summary>
    public static IReadOnlyList<ServiceIssueDto> ParseStoppedAutoServices(string? json, int limit = 20)
    {
        var list = new List<ServiceIssueDto>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var start = Str(e, "start_type") ?? "";
                var status = Str(e, "status") ?? "";
                if (!start.Contains("auto", StringComparison.OrdinalIgnoreCase)) continue;
                if (status.Equals("running", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new ServiceIssueDto(Str(e, "name") ?? "?", Str(e, "display_name"), status, start));
                if (list.Count >= limit) break;
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>boot_time (epoch detik) -> hari uptime; null bila tidak ada atau tidak masuk akal.</summary>
    public static int? UptimeDays(double? bootEpochSeconds, DateTime nowUtc)
    {
        if (bootEpochSeconds is not { } b || b <= 0) return null;
        var boot = DateTime.UnixEpoch.AddSeconds(b);
        var days = (nowUtc - boot).TotalDays;
        return days is < 0 or > 36500 ? null : (int)Math.Floor(days);
    }

    public static bool IsOnline(DateTime? lastSeen, int offlineMinutes, DateTime nowUtc) =>
        lastSeen is { } ls && ls > nowUtc.AddMinutes(-(Math.Max(offlineMinutes, 1) + 1));

    public static string? Truncate(string? s, int max = 4000) => s is null ? null : s.Length <= max ? s : s[..max] + "\n… (dipotong)";

    public static string? Compose(string? moreInfo, string? stdout, string? stderr)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(moreInfo)) parts.Add(moreInfo.Trim());
        if (!string.IsNullOrWhiteSpace(stdout)) parts.Add("STDOUT:\n" + stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr)) parts.Add("STDERR:\n" + stderr.Trim());
        return parts.Count == 0 ? null : Truncate(string.Join("\n\n", parts));
    }

    /// <summary>Kurangi jumlah titik dengan merata-ratakan per bucket, agar respons tetap kecil.</summary>
    public static IReadOnlyList<MetricPoint> Downsample(IReadOnlyList<MetricPoint> pts, int max)
    {
        if (pts.Count <= max || max < 2) return pts;
        var step = (int)Math.Ceiling(pts.Count / (double)max);
        var res = new List<MetricPoint>();
        for (var i = 0; i < pts.Count; i += step)
        {
            var chunk = pts.Skip(i).Take(step).ToList();
            var midTicks = (long)chunk.Average(c => c.Time.Ticks);
            res.Add(new MetricPoint(new DateTime(midTicks, DateTimeKind.Utc), Math.Round(chunk.Average(c => c.Value), 1)));
        }
        return res;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Number ? v.ToString() : null : null;

    private static double? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }
}
