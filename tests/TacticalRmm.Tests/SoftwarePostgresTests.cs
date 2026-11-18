using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class SoftwarePostgresTests
{
    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeBus(object? reply) : IAgentCommandBus
    {
        public string? LastFunc;
        public Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default) { LastFunc = data["func"] as string; return Task.FromResult(reply); }
        public Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeFleet(IReadOnlyList<FleetAssetRow> assets) : IFleetService
    {
        public Task<Outcome<FleetAssetsDto>> GetAssetsAsync(User caller, int? clientId, int? siteId, CancellationToken ct = default) =>
            Task.FromResult(Outcome.Ok(new FleetAssetsDto(new FleetScope(clientId, siteId, "All clients"), assets)));
        public Task<Outcome<FleetSummaryDto>> GetSummaryAsync(User caller, int? clientId, int? siteId, int days, int riskLimit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Outcome<FleetPatchesDto>> GetPatchesAsync(User caller, int? clientId, int? siteId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Outcome<AgentOverviewDto>> GetOverviewAsync(User caller, string agentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Outcome<MetricSeries>> GetMetricsAsync(User caller, string agentId, string metric, string? label, int days, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static async Task<(AppDbContext db, Agent a1, Agent a2)> SeedAsync()
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "One" }; var s = new Site { Name = "S", Client = c };
        var a1 = new Agent { AgentId = "a1", Hostname = "PC1", Site = s }; var a2 = new Agent { AgentId = "a2", Hostname = "PC2", Site = s };
        db.AddRange(c, s, a1, a2);
        await db.SaveChangesAsync();
        return (db, a1, a2);
    }

    private static object Reply(params (string name, string version)[] apps) =>
        apps.Select(a => (object?)new Dictionary<object, object?> { ["name"] = a.name, ["version"] = a.version, ["publisher"] = "P" }).ToList();

    [PostgresFact]
    public async Task RefreshAsksTheAgentAndStoresTheList()
    {
        var (db, a1, _) = await SeedAsync();
        await using var _db = db;
        var bus = new FakeBus(Reply(("Zoom", "5"), ("7-Zip", "23")));
        var svc = new SoftwareService(db, bus, new NoAudit());
        Assert.Empty((await svc.GetAsync("a1")).Value!.Items);
        Assert.Null((await svc.GetAsync("a1")).Value!.UpdatedAt);

        var r = (await svc.RefreshAsync("a1", "admin")).Value!;
        Assert.Equal("softwarelist", bus.LastFunc);
        Assert.Equal(new[] { "7-Zip", "Zoom" }, r.Items.Select(i => i.Name).ToArray());
        var again = (await svc.GetAsync("a1")).Value!;
        Assert.Equal(2, again.Items.Count);
        Assert.NotNull(again.UpdatedAt);
    }

    [PostgresFact]
    public async Task AnAgentThatDoesNotAnswerKeepsItsOldList()
    {
        var (db, _, _) = await SeedAsync();
        await using var _db = db;
        await new SoftwareService(db, new FakeBus(Reply(("Old", "1"))), new NoAudit()).RefreshAsync("a1", "admin");
        foreach (var (reply, expected) in new (object?, OutcomeStatus)[] { ("timeout", OutcomeStatus.Conflict), ("natsdown", OutcomeStatus.Conflict), ("malformed", OutcomeStatus.Invalid), (new Dictionary<object, object?>(), OutcomeStatus.Invalid) })
        {
            var r = await new SoftwareService(db, new FakeBus(reply), new NoAudit()).RefreshAsync("a1", "admin");
            Assert.Equal(expected, r.Status);
        }
        Assert.Equal("Old", (await new SoftwareService(db, new FakeBus(null), new NoAudit()).GetAsync("a1")).Value!.Items.Single().Name);
        Assert.Equal(OutcomeStatus.NotFound, (await new SoftwareService(db, new FakeBus(null), new NoAudit()).RefreshAsync("nope", "admin")).Status);
    }

    [PostgresFact]
    public async Task WhatAnAgentPostsIsCleanedBeforeItIsStored()
    {
        var (db, a1, _) = await SeedAsync();
        await using var _db = db;
        await new AgentTelemetryService(db).SaveSoftwareAsync(a1, """[{"name":"App","version":"1","uninstall":"evil.exe","location":"C:\\"},{"name":""},"junk"]""");
        var stored = await db.InstalledSoftware.AsNoTracking().SingleAsync();
        Assert.Contains("evil.exe", stored.Software);   // kept as text for the Uninstall button; it only ever runs on the agent that posted it
        Assert.Contains("App", stored.Software);
        Assert.NotNull(stored.UpdatedAt);
    }

    [PostgresFact]
    public async Task SoftwareReportHasOneRowPerAssetAndApplicationAndCanSearch()
    {
        var (db, a1, a2) = await SeedAsync();
        await using var _db = db;
        var bus = new FakeBus(null);
        await new SoftwareService(db, new FakeBus(Reply(("Google Chrome", "120"), ("7-Zip", "23"))), new NoAudit()).RefreshAsync("a1", "admin");
        await new SoftwareService(db, new FakeBus(Reply(("Google Chrome", "119"))), new NoAudit()).RefreshAsync("a2", "admin");
        var assets = new[] { "a1", "a2" }.Select((id, i) => new FleetAssetRow(id, $"PC{i + 1}", "One", "S", "Win", null, 90, "good", true, null, 0, 0, false, null, null)).ToList();
        var svc = new ReportService(db, new FakeFleet(assets));
        var admin = new User { Username = "admin", IsSuperuser = true };

        var all = (await svc.BuildAsync(admin, "software", null, null, null, null)).Value!;
        Assert.Equal(3, all.Total);
        Assert.Equal("Client", all.Columns[0]);
        var chrome = (await svc.BuildAsync(admin, "software", null, null, null, null, "chrome")).Value!;
        Assert.Equal(new[] { "PC1", "PC2" }, chrome.Rows.Select(r => r[2]).ToArray());
        Assert.Equal(new[] { "120", "119" }, chrome.Rows.Select(r => r[4]).ToArray());
        var preview = (await svc.BuildAsync(admin, "software", null, null, null, null, null, 1)).Value!;
        Assert.Single(preview.Rows);
        Assert.Equal(3, preview.Total);   // the preview is cut, the total is not
        Assert.Equal(3, (await svc.BuildAsync(admin, "software", null, null, null, null, null, 200)).Value!.Rows.Count);
        var csv = (await svc.CsvAsync(admin, "software", null, null, null, null, "7-zip")).Value!;
        Assert.Contains("7-Zip", System.Text.Encoding.UTF8.GetString(csv.Content));
        Assert.DoesNotContain("Chrome", System.Text.Encoding.UTF8.GetString(csv.Content));
        Assert.Contains("software", svc.Available(admin).Select(x => x.Name));
    }

    [PostgresFact]
    public async Task SchemaUpgradeAddsTheUpdatedAtColumnToAnOldDatabase()
    {
        await using var db = await PgDb.CreateAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE installed_software DROP COLUMN updated_at");
        await SchemaUpgrade.RunAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Assert.Empty(await db.InstalledSoftware.AsNoTracking().ToListAsync());
    }
}
