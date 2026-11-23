using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class FleetInsightsTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);
    private static HealthInput In(bool online = true, double? cpu = 10, double? ram = 20, double? disk = 40, int err = 0, int warn = 0, int pending = 0, bool reboot = false, bool outdated = false) =>
        new(online, cpu, ram, disk, err, warn, pending, reboot, 10, outdated);
    private static HardwareSnapshot Snap(string health = "Healthy", int wear = 10, long? full = null) =>
        new("Dell Inc.", "Latitude 5440", "SN1", null, null, [new DiskInfo("D0", "SSD", 1, health, "OK", 35, wear, 0, 0, 100)], full is null ? null : new BatteryInfo(50000, full, 100));
    private static CategoryScoreDto Cat(IReadOnlyList<CategoryScoreDto> l, string code) => l.Single(c => c.Code == code);

    [Fact]
    public void CategoriesCountTheShareOfFineDevices()
    {
        var devices = new[]
        {
            new FleetInsights.Device(In(), true, Snap()),
            new FleetInsights.Device(In(online: false, pending: 3), true, Snap("Unhealthy")),
            new FleetInsights.Device(In(err: 1, outdated: true), true, null),
            new FleetInsights.Device(In(cpu: null, ram: null, disk: null), false, null),
        };
        var c = FleetInsights.Categories(devices, new HealthWeights());
        Assert.Equal(75, Cat(c, "availability").Score);          // 3 of 4 online
        Assert.Equal(75, Cat(c, "patching").Score);              // one has pending updates
        Assert.Equal(75, Cat(c, "alerts").Score);
        Assert.Equal(50, Cat(c, "hardware").Score);              // 2 checked, 1 fine
        Assert.Equal(2, Cat(c, "hardware").Devices);
        Assert.Equal(3, Cat(c, "resources").Devices);            // the one without any metric is left out
        Assert.Equal(75, Cat(c, "agent").Score);
    }

    [Fact]
    public void ACategoryWithoutDataHasNoScore()
    {
        var c = FleetInsights.Categories([new FleetInsights.Device(In(), true, null)], new HealthWeights());
        Assert.Null(Cat(c, "hardware").Score);
        Assert.Empty(FleetInsights.Categories([], new HealthWeights()).Where(x => x.Score is not null));
    }

    [Fact]
    public void ResourcesFlagBusyDisksCpuAndMemory()
    {
        var c = FleetInsights.Categories([new FleetInsights.Device(In(disk: 90), true, null), new FleetInsights.Device(In(cpu: 85), true, null), new FleetInsights.Device(In(ram: 81), true, null), new FleetInsights.Device(In(), true, null)], new HealthWeights());
        Assert.Equal(25, Cat(c, "resources").Score);
    }

    private static FleetInsights.HardwareItem Item(string host, HardwareSnapshot? s, DateOnly? warranty = null, bool windows = true) => new("id-" + host, host, "Acme", "Main", windows, s, warranty, s is null ? null : DateTime.UtcNow);

    [Fact]
    public void HardwareSummaryCountsRiskWarrantyAndModels()
    {
        var items = new[]
        {
            Item("A", Snap()),
            Item("B", Snap(wear: 85)),                                        // watch (disk)
            Item("C", Snap("Unhealthy")),                                     // high (disk)
            Item("D", Snap(full: 20000), Today.AddDays(-3)),                  // high (battery) + warranty ended
            Item("E", Snap(), Today.AddDays(30)),                             // ok disk, warranty ending soon
            Item("F", null),                                                  // windows, never checked
            Item("G", null, windows: false),                                  // not windows: not counted as unchecked
        };
        var hw = FleetInsights.Hardware(items, Today, 10);
        Assert.Equal(5, hw.Checked); Assert.Equal(1, hw.NotChecked);
        Assert.Equal(1, hw.Ok); Assert.Equal(2, hw.High); Assert.Equal(2, hw.Watch);   // E is watch because of the warranty
        Assert.Equal(2, hw.DiskIssues); Assert.Equal(1, hw.BatteryIssues);
        Assert.Equal(1, hw.WarrantyExpired); Assert.Equal(1, hw.WarrantyEndingSoon);
        Assert.Equal(4, hw.WarrantyUnknown);                                              // Windows devices without a date: A, B, C and F
        Assert.Equal("Dell Inc. Latitude 5440", hw.Models[0].Name); Assert.Equal(5, hw.Models[0].Count);
        Assert.Equal(["C", "D"], hw.AtRisk.Take(2).Select(r => r.Hostname).Order());     // high first
        Assert.DoesNotContain(hw.AtRisk, r => r.Hostname == "A");
    }

    [Fact]
    public void AtRiskListIsCapped() =>
        Assert.Equal(3, FleetInsights.Hardware(Enumerable.Range(0, 10).Select(i => Item("H" + i, Snap("Unhealthy"))).ToList(), Today, 3).AtRisk.Count);
}
