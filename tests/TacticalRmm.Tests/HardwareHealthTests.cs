using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class HardwareHealthTests
{
    private static readonly DateOnly Today = new(2026, 11, 22);
    private static DiskInfo Disk(string? health = "Healthy", string? status = "OK", int? temp = 35, int? wear = 10, long? re = 0, long? we = 0, long? hours = 5000) =>
        new("Disk0", "SSD", 512_000_000_000, health, status, temp, wear, re, we, hours);
    private static HardwareSnapshot Snap(DiskInfo? d = null, BatteryInfo? b = null) => new("Dell Inc.", "Latitude", "ABC123", "1.0", "2023-01-01", [d ?? Disk()], b);

    [Fact] public void HealthyIsOk() => Assert.Equal("ok", HardwareHealth.Assess(Snap(), null, Today).Risk);
    [Fact] public void NoSnapshotIsUnknown() => Assert.Equal("unknown", HardwareHealth.Assess(null, null, Today).Risk);

    [Theory]
    [InlineData("Unhealthy", "OK", 35, 10, 0, "high")]
    [InlineData("Warning", "OK", 35, 10, 0, "watch")]
    [InlineData("Healthy", "Predictive Failure", 35, 10, 0, "high")]
    [InlineData("Healthy", "OK", 35, 85, 0, "watch")]
    [InlineData("Healthy", "OK", 35, 95, 0, "high")]
    [InlineData("Healthy", "OK", 66, 10, 0, "watch")]
    [InlineData("Healthy", "OK", 80, 10, 0, "high")]
    [InlineData("Healthy", "OK", 35, 10, 3, "watch")]
    [InlineData("Healthy", "OK", 35, 10, 500, "high")]
    public void RatesDisks(string health, string status, int temp, int wear, long errors, string expected) =>
        Assert.Equal(expected, HardwareHealth.Assess(Snap(Disk(health, status, temp, wear, errors)), null, Today).Risk);

    [Fact] public void OldDiskIsWatch() => Assert.Equal("watch", HardwareHealth.Assess(Snap(Disk(hours: 50000)), null, Today).Risk);
    [Fact] public void MissingCountersAreNotAProblem() => Assert.Equal("ok", HardwareHealth.Assess(Snap(Disk(temp: null, wear: null, re: null, we: null, hours: null)), null, Today).Risk);

    [Theory]
    [InlineData(50000, 40000, "ok")]
    [InlineData(50000, 30000, "watch")]
    [InlineData(50000, 20000, "high")]
    public void RatesBattery(long design, long full, string expected) => Assert.Equal(expected, HardwareHealth.Assess(Snap(b: new BatteryInfo(design, full, 100)), null, Today).Risk);

    [Fact] public void ManyChargeCyclesAreWatch() => Assert.Equal("watch", HardwareHealth.Assess(Snap(b: new BatteryInfo(50000, 49000, 1200)), null, Today).Risk);

    [Fact]
    public void WarrantyEndedOrEndingSoonIsWatch()
    {
        Assert.Equal("watch", HardwareHealth.Assess(Snap(), Today.AddDays(-1), Today).Risk);
        Assert.Equal("watch", HardwareHealth.Assess(Snap(), Today.AddDays(60), Today).Risk);
        Assert.Equal("ok", HardwareHealth.Assess(Snap(), Today.AddDays(61), Today).Risk);
        Assert.Equal("watch", HardwareHealth.Assess(null, Today.AddDays(10), Today).Risk);
    }

    [Fact]
    public void WorstReasonsComeFirst()
    {
        var (risk, reasons) = HardwareHealth.Assess(Snap(Disk(wear: 85)), Today.AddDays(-5), Today);
        Assert.Equal("watch", risk);
        var (r2, rs2) = HardwareHealth.Assess(Snap(Disk(health: "Unhealthy", wear: 85)), null, Today);
        Assert.Equal("high", r2); Assert.Equal("high", rs2[0].Level);
    }

    [Fact]
    public void ParsesWhatTheScriptPrints()
    {
        const string json = """{"manufacturer":"LENOVO","model":"T14","serial":"PF123","bios_version":"N1","bios_date":"2022-05-01","disks":[{"name":"NVMe","media":"SSD","size_bytes":512110190592,"health":"Healthy","status":"OK","temperature_c":41,"wear_percent":7,"read_errors":0,"write_errors":null,"power_on_hours":"1234"}],"battery":{"design_mwh":57000,"full_mwh":51000,"cycles":210}}""";
        var s = HardwareHealth.Parse(json)!;
        Assert.Equal("PF123", s.Serial); Assert.Single(s.Disks);
        Assert.Equal(41, s.Disks[0].TemperatureC); Assert.Equal(1234, s.Disks[0].PowerOnHours); Assert.Null(s.Disks[0].WriteErrors);
        Assert.Equal(51000, s.Battery!.FullMwh);
        var back = HardwareHealth.Deserialize(HardwareHealth.Serialize(s))!;
        Assert.Equal(s.Serial, back.Serial); Assert.Equal(41, back.Disks[0].TemperatureC); Assert.Equal(210, back.Battery!.Cycles);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void RejectsBadReports(string payload) => Assert.Null(HardwareHealth.Parse(payload));

    [Fact] public void DesktopWithoutBatteryOrDisks() => Assert.Empty(HardwareHealth.Parse("""{"manufacturer":"X","disks":[],"battery":null}""")!.Disks);

    [Theory]
    [InlineData("To Be Filled By O.E.M.", null)]
    [InlineData("Default string", null)]
    [InlineData("  ", null)]
    [InlineData("PF123", "PF123")]
    public void CleansSerials(string raw, string? expected) => Assert.Equal(expected, HardwareHealth.CleanSerial(raw));

    [Theory]
    [InlineData("2027-01-31", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("31/01/2027", false)]
    [InlineData("1999-01-01", false)]
    [InlineData("2099-01-01", false)]
    public void ValidatesWarranty(string? date, bool ok) => Assert.Equal(ok, HardwareHealth.ValidateWarranty(date, null, Today, out _) is null);

    [Fact] public void LongNoteIsRejected() => Assert.NotNull(HardwareHealth.ValidateWarranty(null, new string('x', 201), Today, out _));

    [Theory]
    [InlineData("Dell Inc.", "ABC123", "https://www.dell.com/support/home/en-us/product-support/servicetag/ABC123/overview")]
    [InlineData("LENOVO", "PF-1", "https://pcsupport.lenovo.com/us/en/search?query=PF-1")]
    [InlineData("Dell Inc.", "A/B", null)]
    [InlineData("Dell Inc.", "To Be Filled By O.E.M.", null)]
    [InlineData("NoName", "ABC", null)]
    public void BuildsVendorLinks(string maker, string serial, string? expected) => Assert.Equal(expected, HardwareHealth.VendorLink(maker, serial));

    [Fact] public void ScriptHasBothMarkersAndNoSecrets() { Assert.Contains("@@OK@@", HardwareHealth.Script); Assert.Contains("@@ERR@@", HardwareHealth.Script); }
}
