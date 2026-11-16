using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class CustomFieldPostgresTests
{
    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly User Admin = new() { Username = "admin", IsSuperuser = true };
    private static readonly User Nobody = new() { Username = "nobody" };   // no role: no permissions

    private static async Task<(AppDbContext db, CustomFieldService svc, Client c, Site s, Agent a)> SeedAsync()
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "C" };
        var s = new Site { Name = "S", Client = c };
        var a = new Agent { AgentId = "agent-1", Hostname = "h1", Site = s };
        db.AddRange(c, s, a);
        await db.SaveChangesAsync();
        return (db, new CustomFieldService(db, new NoAudit()), c, s, a);
    }

    [PostgresFact]
    public async Task ValueIsStoredPerTargetAndDefaultApplies()
    {
        var (db, svc, c, s, a) = await SeedAsync();
        await using var _ = db;
        var f = (await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "Owner", Model = "agent", Type = "text", DefaultValue = "IT" })).Value!;

        var before = (await svc.ValuesAsync(Admin, "agent", "agent-1")).Value!.Single();
        Assert.Equal("", before.Value); Assert.Equal("IT", before.Effective);

        var set = await svc.SetValuesAsync(Admin, "agent", "agent-1", new CustomFieldValuesSet { Values = { [f.Id] = " Budi " } });
        Assert.Equal("Budi", set.Value!.Single().Effective);

        // A blank value removes it again, so the default applies.
        var cleared = await svc.SetValuesAsync(Admin, "agent", "agent-1", new CustomFieldValuesSet { Values = { [f.Id] = "" } });
        Assert.Equal("IT", cleared.Value!.Single().Effective);
        Assert.Equal(0, await db.CustomFieldValues.CountAsync());
    }

    [PostgresFact]
    public async Task InvalidValueAndForeignFieldAreRejected()
    {
        var (db, svc, c, s, a) = await SeedAsync();
        await using var _ = db;
        var num = (await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "Seats", Model = "client", Type = "number" })).Value!;
        var agentField = (await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "Tag", Model = "agent", Type = "text" })).Value!;

        var bad = await svc.SetValuesAsync(Admin, "client", c.Id.ToString(), new CustomFieldValuesSet { Values = { [num.Id] = "many" } });
        Assert.Equal(OutcomeStatus.Invalid, bad.Status);
        var wrongModel = await svc.SetValuesAsync(Admin, "client", c.Id.ToString(), new CustomFieldValuesSet { Values = { [agentField.Id] = "x" } });
        Assert.Equal(OutcomeStatus.Invalid, wrongModel.Status);
        Assert.Equal(0, await db.CustomFieldValues.CountAsync());
    }

    [PostgresFact]
    public async Task SameNameIsAllowedOnDifferentModelsButNotTwiceOnOne()
    {
        var (db, svc, _, _, _) = await SeedAsync();
        await using var _db = db;
        Assert.True((await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "Note", Model = "client", Type = "text" })).IsOk);
        Assert.True((await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "Note", Model = "site", Type = "text" })).IsOk);
        Assert.Equal(OutcomeStatus.Conflict, (await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "Note", Model = "client", Type = "number" })).Status);
    }

    [PostgresFact]
    public async Task ChangingChoicesDropsValuesThatAreNoLongerChoices()
    {
        var (db, svc, c, s, a) = await SeedAsync();
        await using var _ = db;
        var f = (await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "Tier", Model = "site", Type = "single", Options = "Gold\nSilver" })).Value!;
        await svc.SetValuesAsync(Admin, "site", s.Id.ToString(), new CustomFieldValuesSet { Values = { [f.Id] = "Silver" } });
        await svc.UpdateAsync(Admin, f.Id, new CustomFieldUpsert { Options = "Gold\nBronze" });
        Assert.Equal("", (await svc.ValuesAsync(Admin, "site", s.Id.ToString())).Value!.Single().Value);
        Assert.Equal(OutcomeStatus.Conflict, (await svc.UpdateAsync(Admin, f.Id, new CustomFieldUpsert { Type = "text" })).Status);
    }

    [PostgresFact]
    public async Task DeletingAFieldRemovesItsValues()
    {
        var (db, svc, c, s, a) = await SeedAsync();
        await using var _ = db;
        var f = (await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "Seats", Model = "client", Type = "number" })).Value!;
        await svc.SetValuesAsync(Admin, "client", c.Id.ToString(), new CustomFieldValuesSet { Values = { [f.Id] = "10" } });
        Assert.True((await svc.DeleteAsync(Admin, f.Id)).IsOk);
        Assert.Equal(0, await db.CustomFieldValues.CountAsync());
    }

    [PostgresFact]
    public async Task CallerWithoutPermissionIsRefused()
    {
        var (db, svc, c, s, a) = await SeedAsync();
        await using var _ = db;
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.ValuesAsync(Nobody, "agent", "agent-1")).Status);
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.SetValuesAsync(Nobody, "client", c.Id.ToString(), new CustomFieldValuesSet())).Status);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ValuesAsync(Admin, "agent", "nope")).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ValuesAsync(Admin, "user", "1")).Status);
    }

    [PostgresFact]
    public async Task SchemaUpgradeCreatesTheCustomFieldTablesOnAnOldDatabase()
    {
        await using var db = await PgDb.CreateAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE custom_field_values");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE custom_fields");
        await TacticalRmm.Infrastructure.Persistence.SchemaUpgrade.RunAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var svc = new CustomFieldService(db, new NoAudit());
        Assert.True((await svc.CreateAsync(Admin, new CustomFieldUpsert { Name = "X", Model = "agent", Type = "text" })).IsOk);
    }
}
