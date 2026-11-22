using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class HardwarePostgresTests
{
    private sealed class Audit : IAuditService
    {
        public readonly List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }
    private sealed class Bus(object? reply) : IAgentCommandBus
    {
        public int Calls;
        public Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default) { Calls++; return Task.FromResult(reply); }
        public Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly User Admin = new() { Id = 1, Username = "admin", IsSuperuser = true };
    private const string Id = "hw-agent-number-000001";
    private const string Report = """@@OK@@{"manufacturer":"Dell Inc.","model":"Latitude","serial":"ABC123","disks":[{"name":"D","media":"SSD","size_bytes":1,"health":"Unhealthy","status":"OK"}],"battery":null}""";

    private static async Task<(HardwareService svc, TacticalRmm.Infrastructure.Persistence.AppDbContext db, Bus bus)> SetupAsync(object? reply, string plat = "windows")
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "One" }; var s = new Site { Name = "S", Client = c };
        db.AddRange(c, s, new Agent { AgentId = Id, Hostname = "PC1", Site = s, Plat = plat, Goarch = "amd64" });
        await db.SaveChangesAsync();
        var bus = new Bus(reply);
        return (new HardwareService(db, bus, new Audit()), db, bus);
    }

    [PostgresFact]
    public async Task Refresh_saves_the_snapshot_and_rates_it()
    {
        var (svc, db, _) = await SetupAsync(Report);
        await using var _d = db;
        var r = await svc.RefreshAsync(Id);
        Assert.True(r.IsOk, r.Error);
        Assert.Equal("high", r.Value!.Risk); Assert.Equal("ABC123", r.Value.Snapshot!.Serial); Assert.NotNull(r.Value.CollectedAt);
        Assert.NotNull(r.Value.VendorLink);
        Assert.Equal(1, db.HardwareInfos.Count());
        await svc.RefreshAsync(Id);
        Assert.Equal(1, db.HardwareInfos.Count()); // updated, not duplicated
    }

    [PostgresFact]
    public async Task Warranty_survives_a_refresh_and_can_be_set_before_any_check()
    {
        var (svc, db, _) = await SetupAsync(Report);
        await using var _d = db;
        var w = await svc.SetWarrantyAsync(Admin, Id, new WarrantyRequest { EndDate = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"), Note = "3y" });
        Assert.True(w.IsOk); Assert.Equal("unknown", w.Value!.Risk); Assert.Equal("3y", w.Value.WarrantyNote);
        var r = await svc.RefreshAsync(Id);
        Assert.NotNull(r.Value!.WarrantyEnd);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.SetWarrantyAsync(Admin, Id, new WarrantyRequest { EndDate = "tomorrow" })).Status);
        var cleared = await svc.SetWarrantyAsync(Admin, Id, new WarrantyRequest { EndDate = "" });
        Assert.Null(cleared.Value!.WarrantyEnd);
    }

    [PostgresFact]
    public async Task Refresh_reports_offline_bad_output_and_non_windows()
    {
        var (a, da, _) = await SetupAsync("timeout"); await using var _a = da;
        Assert.Equal(OutcomeStatus.Conflict, (await a.RefreshAsync(Id)).Status);
        var (b, db2, _) = await SetupAsync("@@OK@@not json"); await using var _b = db2;
        Assert.Equal(OutcomeStatus.Invalid, (await b.RefreshAsync(Id)).Status);
        var (c, dc, bus) = await SetupAsync(Report, "linux"); await using var _c = dc;
        Assert.Equal(OutcomeStatus.Invalid, (await c.RefreshAsync(Id)).Status); Assert.Equal(0, bus.Calls);
        Assert.Equal(OutcomeStatus.NotFound, (await c.RefreshAsync("nope")).Status);
    }

    [PostgresFact]
    public async Task List_puts_the_worst_first()
    {
        var (svc, db, _) = await SetupAsync(Report);
        await using var _d = db;
        await svc.RefreshAsync(Id);
        var l = (await svc.ListAsync(Admin)).Value!;
        Assert.Single(l); Assert.Equal("high", l[0].Risk); Assert.Equal("One", l[0].Client);
    }
}
