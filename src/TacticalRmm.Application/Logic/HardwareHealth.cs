using System.Text.Json;
using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Application.Logic;

/// <summary>Hardware check of a Windows asset: the PowerShell that collects it, reading what it prints, and the rules that turn it into a risk.
/// These are rules of thumb on what Windows reports (storage reliability counters, battery capacity), not a guarantee that a part will or will not fail.</summary>
public static class HardwareHealth
{
    public const string Ok = "ok", Watch = "watch", High = "high", Unknown = "unknown";
    public const int WarrantySoonDays = 60;

    public const string Script = @"$ErrorActionPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; try {
$cs = Get-CimInstance Win32_ComputerSystem; $bios = Get-CimInstance Win32_BIOS
$disks = @(Get-PhysicalDisk | ForEach-Object { $d = $_; $c = $d | Get-StorageReliabilityCounter
  [pscustomobject]@{ name = [string]$d.FriendlyName; media = [string]$d.MediaType; size_bytes = [int64]$d.Size; health = [string]$d.HealthStatus; status = (@($d.OperationalStatus) -join ', ');
    temperature_c = $c.Temperature; wear_percent = $c.Wear; read_errors = $c.ReadErrorsTotal; write_errors = $c.WriteErrorsTotal; power_on_hours = $c.PowerOnHours } })
$full = Get-CimInstance -Namespace root\wmi -ClassName BatteryFullChargedCapacity | Select-Object -First 1
$stat = Get-CimInstance -Namespace root\wmi -ClassName BatteryStaticData | Select-Object -First 1
$cyc = Get-CimInstance -Namespace root\wmi -ClassName BatteryCycleCount | Select-Object -First 1
$bat = $null
if ($stat -or $full) { $bat = [pscustomobject]@{ design_mwh = $stat.DesignedCapacity; full_mwh = $full.FullChargedCapacity; cycles = $cyc.CycleCount } }
$bd = $null; if ($bios.ReleaseDate) { $bd = $bios.ReleaseDate.ToUniversalTime().ToString('yyyy-MM-dd') }
$o = [pscustomobject]@{ manufacturer = $cs.Manufacturer; model = $cs.Model; serial = $bios.SerialNumber; bios_version = $bios.SMBIOSBIOSVersion; bios_date = $bd; disks = $disks; battery = $bat }
'@@OK@@' + (ConvertTo-Json -InputObject $o -Compress -Depth 4)
} catch { '@@ERR@@ ' + $_.Exception.Message }";

    private static readonly string[] JunkSerials = ["to be filled by o.e.m.", "default string", "system serial number", "none", "0", "n/a", "not applicable", "serial number", "123456789"];

    /// <summary>Makers put placeholders in the serial field; those are not serials.</summary>
    public static string? CleanSerial(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) || JunkSerials.Contains(t.ToLowerInvariant()) || t.Length > 60 || t.Any(char.IsControl) ? null : t;
    }

    public static HardwareSnapshot? Parse(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            var disks = new List<DiskInfo>();
            if (Arr(r, "disks") is { } da)
                foreach (var d in da.EnumerateArray().Take(32))
                    if (d.ValueKind == JsonValueKind.Object)
                        disks.Add(new DiskInfo(Str(d, "name"), Str(d, "media"), Lng(d, "size_bytes"), Str(d, "health"), Str(d, "status"), (int?)Lng(d, "temperature_c"), (int?)Lng(d, "wear_percent"),
                            Lng(d, "read_errors"), Lng(d, "write_errors"), Lng(d, "power_on_hours")));
            BatteryInfo? bat = null;
            if (r.TryGetProperty("battery", out var b) && b.ValueKind == JsonValueKind.Object)
                bat = new BatteryInfo(Lng(b, "design_mwh"), Lng(b, "full_mwh"), (int?)Lng(b, "cycles"));
            return new HardwareSnapshot(Str(r, "manufacturer"), Str(r, "model"), CleanSerial(Str(r, "serial")), Str(r, "bios_version"), Str(r, "bios_date"), disks, bat);
        }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? Cap(v.GetString()) : null;
    private static string? Cap(string? s) => s is null ? null : (s.Length > 200 ? s[..200] : s);
    private static JsonElement? Arr(JsonElement e, string k)
    {
        if (!e.TryGetProperty(k, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Array) return v;
        return null;
    }
    private static long? Lng(JsonElement e, string k)
    {
        if (!e.TryGetProperty(k, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var n) ? n : v.TryGetDouble(out var d) ? (long)d : null;
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s) ? s : null;
    }

    public static string Serialize(HardwareSnapshot s) => JsonSerializer.Serialize(s, Json);
    public static HardwareSnapshot? Deserialize(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        try { return JsonSerializer.Deserialize<HardwareSnapshot>(data, Json); } catch (JsonException) { return null; }
    }
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };

    private static string Gb(long? bytes) => bytes is { } b ? $"{b / 1_000_000_000.0:0} GB" : "disk";

    /// <summary>The reasons to worry (worst first) and the overall risk. A null snapshot is "unknown" but a warranty date still counts.</summary>
    public static (string Risk, List<HardwareReasonDto> Reasons) Assess(HardwareSnapshot? s, DateOnly? warrantyEnd, DateOnly today)
    {
        var rs = new List<HardwareReasonDto>();
        void Add(string level, string text) => rs.Add(new HardwareReasonDto(level, text));
        if (s is not null)
        {
            foreach (var d in s.Disks)
            {
                var n = string.IsNullOrWhiteSpace(d.Name) ? Gb(d.SizeBytes) : d.Name!;
                if (string.Equals(d.Health, "Unhealthy", StringComparison.OrdinalIgnoreCase)) Add(High, $"{n}: Windows reports the disk as unhealthy.");
                else if (string.Equals(d.Health, "Warning", StringComparison.OrdinalIgnoreCase)) Add(Watch, $"{n}: Windows reports a health warning.");
                var st = d.Status ?? "";
                if (st.Contains("Predictive", StringComparison.OrdinalIgnoreCase) || st.Contains("Failed", StringComparison.OrdinalIgnoreCase) || st.Contains("IO Error", StringComparison.OrdinalIgnoreCase) || st.Contains("Lost Communication", StringComparison.OrdinalIgnoreCase))
                    Add(High, $"{n}: status {st}.");
                if (d.WearPercent is { } w) { if (w >= 90) Add(High, $"{n}: {w}% of its write life is used."); else if (w >= 80) Add(Watch, $"{n}: {w}% of its write life is used."); }
                if (d.TemperatureC is { } t and > 0) { if (t >= 75) Add(High, $"{n}: running at {t} °C."); else if (t >= 65) Add(Watch, $"{n}: running warm at {t} °C."); }
                var errs = (d.ReadErrors ?? 0) + (d.WriteErrors ?? 0);
                if (errs >= 100) Add(High, $"{n}: {errs} uncorrected read/write errors.");
                else if (errs > 0) Add(Watch, $"{n}: {errs} uncorrected read/write errors.");
                if (d.PowerOnHours is { } h and >= 40000) Add(Watch, $"{n}: powered on for {h / 8760.0:0.0} years.");
            }
            if (s.Battery is { DesignMwh: > 0, FullMwh: > 0 } b)
            {
                var pct = (int)Math.Round(100.0 * b.FullMwh!.Value / b.DesignMwh!.Value);
                if (pct < 50) Add(High, $"Battery holds only {pct}% of its original capacity.");
                else if (pct < 70) Add(Watch, $"Battery holds {pct}% of its original capacity.");
                if (b.Cycles is { } c and >= 1000) Add(Watch, $"Battery has been through {c} charge cycles.");
            }
        }
        if (warrantyEnd is { } we)
        {
            var left = we.DayNumber - today.DayNumber;
            if (left < 0) Add(Watch, $"Warranty ended on {we:yyyy-MM-dd}.");
            else if (left <= WarrantySoonDays) Add(Watch, $"Warranty ends in {left} day(s), on {we:yyyy-MM-dd}.");
        }
        rs = rs.OrderBy(r => r.Level == High ? 0 : 1).ToList();
        var risk = rs.Any(r => r.Level == High) ? High : rs.Count > 0 ? Watch : s is null ? Unknown : Ok;
        return (risk, rs);
    }

    /// <summary>yyyy-MM-dd, empty = no date. Returns an error text or null.</summary>
    public static string? ValidateWarranty(string? endDate, string? note, DateOnly today, out DateOnly? parsed)
    {
        parsed = null;
        if (note is not null && (note.Length > 200 || note.Any(char.IsControl))) return "The note must be 200 characters or less.";
        if (string.IsNullOrWhiteSpace(endDate)) return null;
        if (!DateOnly.TryParseExact(endDate.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return "Enter the date as yyyy-MM-dd.";
        if (d.Year < 2000 || d > today.AddYears(15)) return "That date is not plausible.";
        parsed = d;
        return null;
    }

    /// <summary>The maker's warranty check page for the serial, or null when the maker is unknown. The server does not call these pages.</summary>
    public static string? VendorLink(string? manufacturer, string? serial)
    {
        if (CleanSerial(serial) is not { } sn || !sn.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return null;
        var m = (manufacturer ?? "").ToLowerInvariant();
        if (m.Contains("hewlett") || m.StartsWith("hp")) return "https://support.hp.com/us-en/checkwarranty";
        if (m.Contains("dell")) return $"https://www.dell.com/support/home/en-us/product-support/servicetag/{sn}/overview";
        if (m.Contains("lenovo")) return $"https://pcsupport.lenovo.com/us/en/search?query={sn}";
        if (m.Contains("asus")) return "https://www.asus.com/support/warranty-status-inquiry/";
        if (m.Contains("acer")) return "https://www.acer.com/us-en/support/warranty";
        return null;
    }
}
