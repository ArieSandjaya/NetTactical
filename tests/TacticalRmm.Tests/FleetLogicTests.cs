using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using Xunit;

namespace TacticalRmm.Tests;

/// <summary>Health score, metric rules, and agent data parser for the asset dashboard.</summary>
public class FleetLogicTests
{
    private static readonly HealthWeights W = new();
    private static readonly DateTime Now = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    private static HealthInput Ok() => new(true, 30, 50, 50, 0, 0, 0, false, 10, false);

    [Fact]
    public void HealthyDeviceScores100()
    {
        var r = HealthScorer.Score(Ok(), W);
        Assert.True(r.Score == 100, "score 100");
        Assert.True(r.Level == HealthScorer.Healthy, "level healthy");
        Assert.True(r.Factors.Count == 0, "no factors");
    }

    [Fact]
    public void OfflineDeviceIsPenalisedAndStaleMetricsIgnored()
    {
        var r = HealthScorer.Score(Ok() with { Online = false, DiskWorstUsed = 99, Cpu = 99 }, W);
        Assert.True(r.Factors.Any(f => f.Code == "offline"), "offline factor");
        Assert.True(!r.Factors.Any(f => f.Code is "disk_high" or "cpu_high"), "stale metrics ignored");
        Assert.True(r.Score == 55, "100-45");
    }

    [Fact]
    public void DiskTiers()
    {
        int P(double used) => 100 - HealthScorer.Score(Ok() with { DiskWorstUsed = used }, W).Score;
        Assert.True(P(79) == 0 && P(80) == 5 && P(85) == 10 && P(90) == 20 && P(95) == 25, "disk tiers");
    }

    [Fact]
    public void AlertsAreCapped()
    {
        var r = HealthScorer.Score(Ok() with { ErrorAlerts = 10, WarningAlerts = 10 }, W);
        Assert.True(r.Score == 100 - 24 - 9, "alert cap");
    }

    [Fact]
    public void ScoreNeverGoesBelowZeroAndLevelsFollowThresholds()
    {
        var worst = new HealthInput(false, null, null, null, 99, 99, 50, true, 400, true);
        Assert.True(HealthScorer.Score(worst, W).Score >= 0, "not negative");
        Assert.True(HealthScorer.LevelOf(80, W) == HealthScorer.Healthy, "80 healthy");
        Assert.True(HealthScorer.LevelOf(79, W) == HealthScorer.Attention, "79 attention");
        Assert.True(HealthScorer.LevelOf(59, W) == HealthScorer.AtRisk, "59 at risk");
    }

    [Fact]
    public void FactorsAreSortedByPenalty()
    {
        var r = HealthScorer.Score(Ok() with { DiskWorstUsed = 96, Cpu = 85, NeedsReboot = true, PendingUpdates = 7 }, W);
        Assert.True(r.Factors[0].Code == "disk_high", "largest first");
        Assert.True(r.Factors.Zip(r.Factors.Skip(1)).All(p => p.First.Penalty >= p.Second.Penalty), "descending order");
    }

    [Fact]
    public void CustomWeightsChangeTheScore()
    {
        var w = new HealthWeights { NeedsReboot = 20 };
        Assert.True(HealthScorer.Score(Ok() with { NeedsReboot = true }, w).Score == 80, "custom weight");
    }

    [Fact]
    public void MetricRulesPickUsedPercent()
    {
        Assert.True(MetricRules.UsedPercent(CheckType.CpuLoad, new CheckResultPayload { Percent = 42 }) == 42, "cpu");
        Assert.True(MetricRules.UsedPercent(CheckType.Memory, new CheckResultPayload { Percent = 130 }) == 100, "clamped to 100");
        Assert.True(MetricRules.UsedPercent(CheckType.DiskSpace, new CheckResultPayload { Exists = true, PercentUsed = 77.46 }) == 77.5, "disk");
        Assert.True(MetricRules.UsedPercent(CheckType.DiskSpace, new CheckResultPayload { Exists = false, PercentUsed = 50 }) is null, "disk missing");
        Assert.True(MetricRules.UsedPercent(CheckType.Ping, new CheckResultPayload { Percent = 5 }) is null, "not a metric");
        Assert.True(MetricRules.FromApiName("RAM") == CheckType.Memory && MetricRules.FromApiName("x") is null, "api name");
    }

    [Fact]
    public void ParsesDisksAndIgnoresGarbage()
    {
        var d = AgentDataParser.ParseDisks("[{\"device\":\"C:\",\"fstype\":\"NTFS\",\"total\":\"223 GB\",\"percent\":91},{\"device\":\"D:\",\"percent\":\"40.5\"},{\"device\":\"E:\"},5]");
        Assert.True(d.Count == 2 && d[0].Percent == 91 && d[1].Percent == 40.5, "two valid disks");
        Assert.True(AgentDataParser.WorstDiskUsed(d) == 91, "worst");
        Assert.True(AgentDataParser.ParseDisks("not json").Count == 0 && AgentDataParser.ParseDisks(null).Count == 0, "bad data is safe");
        Assert.True(AgentDataParser.WorstDiskUsed(new List<DiskDto>()) is null, "empty gives null");
    }

    [Fact]
    public void FindsStoppedAutomaticServices()
    {
        var s = AgentDataParser.ParseStoppedAutoServices("[{\"name\":\"A\",\"status\":\"stopped\",\"start_type\":\"Automatic\"},{\"name\":\"B\",\"status\":\"running\",\"start_type\":\"Automatic\"},{\"name\":\"C\",\"status\":\"stopped\",\"start_type\":\"Manual\"}]");
        Assert.True(s.Count == 1 && s[0].Name == "A", "only stopped automatic services");
    }

    [Fact]
    public void UptimeAndOnlineRules()
    {
        var boot = (Now.AddDays(-212) - DateTime.UnixEpoch).TotalSeconds;
        Assert.True(AgentDataParser.UptimeDays(boot, Now) == 212, "uptime days");
        Assert.True(AgentDataParser.UptimeDays(null, Now) is null && AgentDataParser.UptimeDays(0, Now) is null, "no boot_time");
        Assert.True(AgentDataParser.UptimeDays((Now.AddDays(5) - DateTime.UnixEpoch).TotalSeconds, Now) is null, "boot time in the future rejected");
        Assert.True(AgentDataParser.IsOnline(Now.AddMinutes(-3), 4, Now), "online within threshold");
        Assert.True(!AgentDataParser.IsOnline(Now.AddMinutes(-10), 4, Now), "offline past threshold");
        Assert.True(!AgentDataParser.IsOnline(null, 4, Now), "never reported");
    }

    [Fact]
    public void ComposeAndDownsample()
    {
        Assert.True(AgentDataParser.Compose(null, null, null) is null, "empty");
        var o = AgentDataParser.Compose("info", "out", "err")!;
        Assert.True(o.Contains("STDOUT:\nout") && o.Contains("STDERR:\nerr") && o.StartsWith("info"), "combined");
        Assert.True(AgentDataParser.Truncate(new string('x', 5000))!.Length < 4100, "truncated");

        var pts = Enumerable.Range(0, 5000).Select(i => new MetricPoint(Now.AddMinutes(i), i % 100)).ToList();
        var ds = AgentDataParser.Downsample(pts, 1000);
        Assert.True(ds.Count <= 1000 && ds.Count > 500, "point count reduced");
        Assert.True(AgentDataParser.Downsample(pts.Take(10).ToList(), 1000).Count == 10, "small input unchanged");
    }
}
