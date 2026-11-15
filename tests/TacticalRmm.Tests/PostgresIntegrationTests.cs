using Microsoft.EntityFrameworkCore;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class PostgresIntegrationTests
{
    [PostgresFact]
    public async Task SchemaUpgradeCanRunTwiceOnAFreshDatabase()
    {
        await using var db = await PgDb.CreateAsync();
        var log = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        await SchemaUpgrade.RunAsync(db, log);
        await SchemaUpgrade.RunAsync(db, log);
    }

    [PostgresFact]
    public async Task SchemaUpgradeBringsAnOldDatabaseUpToDate()
    {
        await using var db = await PgDb.CreateAsync();
        // Simulate a database made before v6/v7.
        foreach (var sql in new[]
        {
            "ALTER TABLE clients DROP COLUMN policy_id", "ALTER TABLE sites DROP COLUMN policy_id",
            "ALTER TABLE clients DROP COLUMN alert_template_id", "ALTER TABLE sites DROP COLUMN alert_template_id",
            "DROP TABLE alert_templates",
        })
            await db.Database.ExecuteSqlRawAsync(sql);
        await SchemaUpgrade.RunAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var c = new Client { Name = "C" };
        var t = new AlertTemplate { Name = "T", FlagCheckFailures = true };
        db.AddRange(c, t);
        await db.SaveChangesAsync();
        c.AlertTemplateId = t.Id;
        await db.SaveChangesAsync();
        Assert.Equal(t.Id, await db.Clients.AsNoTracking().Where(x => x.Id == c.Id).Select(x => x.AlertTemplateId).SingleAsync());
    }

    private static async Task<(AppDbContext db, Client c, Site s, Agent a, Policy pa, Policy ps, Policy pc)> SeedAsync()
    {
        var db = await PgDb.CreateAsync();
        var pa = new Policy { Name = "agent", Active = true };
        var ps = new Policy { Name = "site", Active = true };
        var pc = new Policy { Name = "client", Active = true };
        var c = new Client { Name = "C" };
        var s = new Site { Name = "S", Client = c };
        var a = new Agent { AgentId = "a1", Hostname = "h1", Site = s };
        db.AddRange(pa, ps, pc, c, s, a);
        await db.SaveChangesAsync();
        return (db, c, s, a, pa, ps, pc);
    }

    [PostgresFact]
    public async Task PolicyResolvesAgentThenSiteThenClient()
    {
        var (db, c, s, a, pa, ps, pc) = await SeedAsync();
        await using var _ = db;
        Assert.Null(await EffectivePolicy.IdForAsync(db, a));

        c.PolicyId = pc.Id; await db.SaveChangesAsync();
        Assert.Equal(pc.Id, await EffectivePolicy.IdForAsync(db, a));

        s.PolicyId = ps.Id; await db.SaveChangesAsync();
        Assert.Equal(ps.Id, await EffectivePolicy.IdForAsync(db, a));

        a.PolicyId = pa.Id; await db.SaveChangesAsync();
        Assert.Equal(pa.Id, await EffectivePolicy.IdForAsync(db, a));
        Assert.Equal(pa.Id, (await EffectivePolicy.MapAsync(db))[a.Id]);
        Assert.Equal(new[] { a.Id }, await EffectivePolicy.AgentIdsAsync(db, pa.Id));
        Assert.Empty(await EffectivePolicy.AgentIdsAsync(db, ps.Id));
    }

    [PostgresFact]
    public async Task InactivePolicyIsSkippedAndBlockSwitchesEverythingOff()
    {
        var (db, c, s, a, pa, ps, pc) = await SeedAsync();
        await using var _ = db;
        c.PolicyId = pc.Id; s.PolicyId = ps.Id; a.PolicyId = pa.Id;
        ps.Active = false; pa.Active = false;
        await db.SaveChangesAsync();
        Assert.Equal(pc.Id, await EffectivePolicy.IdForAsync(db, a));
        Assert.Equal(pc.Id, (await EffectivePolicy.MapAsync(db))[a.Id]);

        a.BlockPolicyInheritance = true; await db.SaveChangesAsync();
        Assert.Null(await EffectivePolicy.IdForAsync(db, a));
        Assert.Empty(await EffectivePolicy.MapAsync(db));
    }

    [PostgresFact]
    public async Task DeletingAPolicyClearsItFromClientAndSite()
    {
        var (db, c, s, a, _, ps, pc) = await SeedAsync();
        await using var _db = db;
        c.PolicyId = pc.Id; s.PolicyId = ps.Id; await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM policies WHERE id = {0} OR id = {1}", ps.Id, pc.Id);
        Assert.Null(await db.Clients.AsNoTracking().Where(x => x.Id == c.Id).Select(x => x.PolicyId).SingleAsync());
        Assert.Null(await db.Sites.AsNoTracking().Where(x => x.Id == s.Id).Select(x => x.PolicyId).SingleAsync());
    }

    [PostgresFact]
    public async Task AlertTemplateResolvesSiteThenClientThenDefault()
    {
        var (db, c, s, a, _, _, _) = await SeedAsync();
        await using var _db = db;
        Assert.Null(await AlertTemplateResolver.FlagsAsync(db, a));

        var def = new AlertTemplate { Name = "def", IsDefault = true, FlagAgentOverdue = true };
        var cli = new AlertTemplate { Name = "cli", FlagCheckFailures = true };
        var sit = new AlertTemplate { Name = "sit", FlagTaskFailures = true };
        db.AddRange(def, cli, sit); await db.SaveChangesAsync();

        Assert.True((await AlertTemplateResolver.FlagsAsync(db, a))!.AgentOverdue);
        c.AlertTemplateId = cli.Id; await db.SaveChangesAsync();
        Assert.True((await AlertTemplateResolver.FlagsAsync(db, a))!.CheckFailures);
        s.AlertTemplateId = sit.Id; await db.SaveChangesAsync();
        Assert.True((await AlertTemplateResolver.FlagsAsync(db, a))!.TaskFailures);

        sit.Enabled = false; await db.SaveChangesAsync();
        Assert.True((await AlertTemplateResolver.FlagsAsync(db, a))!.CheckFailures);
    }
}
