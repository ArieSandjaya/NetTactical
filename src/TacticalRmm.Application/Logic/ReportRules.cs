using System.Globalization;
using System.Text;

namespace TacticalRmm.Application.Logic;

/// <summary>Report helpers: CSV text that is safe to open in a spreadsheet, and the date range of a report. Pure logic.</summary>
public static class ReportRules
{
    public const int MaxRangeDays = 366;

    /// <summary>
    /// One CSV cell. Texts that a spreadsheet would run as a formula (starting with = + - @ tab or carriage return) get a leading apostrophe,
    /// because hostnames and alert messages come from agents and must not be able to run anything. Plain numbers are left alone.
    /// </summary>
    public static string Cell(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0]) && !decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) v = "'" + v;
        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    /// <summary>The whole file: a header row and the data rows, CRLF line ends.</summary>
    public static string Csv(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", columns.Select(Cell))).Append("\r\n");
        foreach (var r in rows) sb.Append(string.Join(",", r.Select(Cell))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>The period of a report: dates are inclusive, default is the last 30 days, at most a year.</summary>
    public static (DateTime From, DateTime ToExclusive, string? Error) Range(string? from, string? to, DateTime nowUtc)
    {
        var today = nowUtc.Date;
        DateTime f = today.AddDays(-30), t = today;
        if (!string.IsNullOrWhiteSpace(from) && !DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out f)) return (default, default, "from must be a date like 2026-11-01");
        if (!string.IsNullOrWhiteSpace(to) && !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return (default, default, "to must be a date like 2026-11-30");
        if (f > t) return (default, default, "from is after to");
        if ((t - f).TotalDays >= MaxRangeDays) return (default, default, $"the period can be at most {MaxRangeDays} days");
        // UTC kind is required: PostgreSQL (timestamp with time zone) refuses a DateTime of unspecified kind.
        return (DateTime.SpecifyKind(f, DateTimeKind.Utc), DateTime.SpecifyKind(t.AddDays(1), DateTimeKind.Utc), null);
    }
}
