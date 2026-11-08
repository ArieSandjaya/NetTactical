using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using Xunit;

namespace TacticalRmm.Tests;

/// <summary>Skor kesehatan, aturan metrik, dan parser data agent untuk dashboard aset.</summary>
public class FleetLogicTests
{
    private static readonly HealthWeights W = new();
    private static readonly DateTime Now = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    private static HealthInput Ok() => new(true, 30, 50, 50, 0, 0, 0, false, 10, false);

    [Fact]
    public void HealthyDeviceScores100()
    {
        var r = HealthScorer.Score(Ok(), W);
        Assert.True(r.Score == 100, "skor 100");
        Assert.True(r.Level == HealthScorer.Healthy, "level healthy");
        Assert.True(r.Factors.Count == 0, "tanpa faktor");
    }

    [Fact]
    public void OfflineDeviceIsPenalisedAndStaleMetricsIgnored()
    {
        var r = HealthScorer.Score(Ok() with { Online = false, DiskWorstUsed = 99, Cpu = 99 }, W);
        Assert.True(r.Factors.Any(f => f.Code == "offline"), "faktor offline");
        Assert.True(!r.Factors.Any(f => f.Code is "disk_high" or "cpu_high"), "metrik lama diabaikan");
        Assert.True(r.Score == 55, "100-45");
    }

    [Fact]
    public void DiskTiers()
    {
        int P(double used) => 100 - HealthScorer.Score(Ok() with { DiskWorstUsed = used }, W).Score;
        Assert.True(P(79) == 0 && P(80) == 5 && P(85) == 10 && P(90) == 20 && P(95) == 25, "tangga disk");
    }

    [Fact]
    public void AlertsAreCapped()
    {
        var r = HealthScorer.Score(Ok() with { ErrorAlerts = 10, WarningAlerts = 10 }, W);
        Assert.True(r.Score == 100 - 24 - 9, "batas alert");
    }

    [Fact]
    public void ScoreNeverGoesBelowZeroAndLevelsFollowThresholds()
    {
        var worst = new HealthInput(false, null, null, null, 99, 99, 50, true, 400, true);
        Assert.True(HealthScorer.Score(worst, W).Score >= 0, "tidak negatif");
        Assert.True(HealthScorer.LevelOf(80, W) == HealthScorer.Healthy, "80 sehat");
        Assert.True(HealthScorer.LevelOf(79, W) == HealthScorer.Attention, "79 perhatian");
        Assert.True(HealthScorer.LevelOf(59, W) == HealthScorer.AtRisk, "59 berisiko");
    }

    [Fact]
    public void FactorsAreSortedByPenalty()
    {
        var r = HealthScorer.Score(Ok() with { DiskWorstUsed = 96, Cpu = 85, NeedsReboot = true, PendingUpdates = 7 }, W);
        Assert.True(r.Factors[0].Code == "disk_high", "terbesar dulu");
        Assert.True(r.Factors.Zip(r.Factors.Skip(1)).All(p => p.First.Penalty >= p.Second.Penalty), "urut menurun");
    }

    [Fact]
    public void CustomWeightsChangeTheScore()
    {
        var w = new HealthWeights { NeedsReboot = 20 };
        Assert.True(HealthScorer.Score(Ok() with { NeedsReboot = true }, w).Score == 80, "bobot kustom");
    }

    [Fact]
    public void MetricRulesPickUsedPercent()
    {
        Assert.True(MetricRules.UsedPercent(CheckType.CpuLoad, new CheckResultPayload { Percent = 42 }) == 42, "cpu");
        Assert.True(MetricRules.UsedPercent(CheckType.Memory, new CheckResultPayload { Percent = 130 }) == 100, "dijepit ke 100");
        Assert.True(MetricRules.UsedPercent(CheckType.DiskSpace, new CheckResultPayload { Exists = true, PercentUsed = 77.46 }) == 77.5, "disk");
        Assert.True(MetricRules.UsedPercent(CheckType.DiskSpace, new CheckResultPayload { Exists = false, PercentUsed = 50 }) is null, "disk tak ada");
        Assert.True(MetricRules.UsedPercent(CheckType.Ping, new CheckResultPayload { Percent = 5 }) is null, "bukan metrik");
        Assert.True(MetricRules.FromApiName("RAM") == CheckType.Memory && MetricRules.FromApiName("x") is null, "nama api");
    }

    [Fact]
    public void ParsesDisksAndIgnoresGarbage()
    {
        var d = AgentDataParser.ParseDisks("[{\"device\":\"C:\",\"fstype\":\"NTFS\",\"total\":\"223 GB\",\"percent\":91},{\"device\":\"D:\",\"percent\":\"40.5\"},{\"device\":\"E:\"},5]");
        Assert.True(d.Count == 2 && d[0].Percent == 91 && d[1].Percent == 40.5, "dua disk valid");
        Assert.True(AgentDataParser.WorstDiskUsed(d) == 91, "terburuk");
        Assert.True(AgentDataParser.ParseDisks("bukan json").Count == 0 && AgentDataParser.ParseDisks(null).Count == 0, "data buruk aman");
        Assert.True(AgentDataParser.WorstDiskUsed(new List<DiskDto>()) is null, "kosong null");
    }

    [Fact]
    public void FindsStoppedAutomaticServices()
    {
        var s = AgentDataParser.ParseStoppedAutoServices("[{\"name\":\"A\",\"status\":\"stopped\",\"start_type\":\"Automatic\"},{\"name\":\"B\",\"status\":\"running\",\"start_type\":\"Automatic\"},{\"name\":\"C\",\"status\":\"stopped\",\"start_type\":\"Manual\"}]");
        Assert.True(s.Count == 1 && s[0].Name == "A", "hanya layanan otomatis yang berhenti");
    }

    [Fact]
    public void UptimeAndOnlineRules()
    {
        var boot = (Now.AddDays(-212) - DateTime.UnixEpoch).TotalSeconds;
        Assert.True(AgentDataParser.UptimeDays(boot, Now) == 212, "uptime hari");
        Assert.True(AgentDataParser.UptimeDays(null, Now) is null && AgentDataParser.UptimeDays(0, Now) is null, "tanpa boot_time");
        Assert.True(AgentDataParser.UptimeDays((Now.AddDays(5) - DateTime.UnixEpoch).TotalSeconds, Now) is null, "boot di masa depan ditolak");
        Assert.True(AgentDataParser.IsOnline(Now.AddMinutes(-3), 4, Now), "online dalam ambang");
        Assert.True(!AgentDataParser.IsOnline(Now.AddMinutes(-10), 4, Now), "offline lewat ambang");
        Assert.True(!AgentDataParser.IsOnline(null, 4, Now), "belum pernah lapor");
    }

    [Fact]
    public void ComposeAndDownsample()
    {
        Assert.True(AgentDataParser.Compose(null, null, null) is null, "kosong");
        var o = AgentDataParser.Compose("info", "out", "err")!;
        Assert.True(o.Contains("STDOUT:\nout") && o.Contains("STDERR:\nerr") && o.StartsWith("info"), "gabungan");
        Assert.True(AgentDataParser.Truncate(new string('x', 5000))!.Length < 4100, "dipotong");

        var pts = Enumerable.Range(0, 5000).Select(i => new MetricPoint(Now.AddMinutes(i), i % 100)).ToList();
        var ds = AgentDataParser.Downsample(pts, 1000);
        Assert.True(ds.Count <= 1000 && ds.Count > 500, "jumlah titik berkurang");
        Assert.True(AgentDataParser.Downsample(pts.Take(10).ToList(), 1000).Count == 10, "sedikit tidak diubah");
    }
}
