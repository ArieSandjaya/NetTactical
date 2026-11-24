using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class AssetGroupPostgresTests
{
    private sealed class Perms(params string[] deny) : IPermissionService
    {
        public bool HasPermission(User user, string permission) => true;
        public Task<bool> HasPermissionOnAgentAsync(User user, string agentId, CancellationToken ct = default) => Task.FromResult(!deny.Contains(agentId));
    }
    private sealed class Audit : IAuditService
    {
        public readonly List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }

    private const string A = "grp-agent-number-000001", B = "grp-agent-number-000002", C = "grp-agent-number-000003";
    private static readonly User Admin = new() { Id = 1, Username = "admin", IsSuperuser = true };

    private static async Task<(AssetGroupService svc, AppDbContext db, Audit audit, Policy sitePol, Policy groupPol)> SetupAsync(params string[] deny)
    {
        var db = await PgDb.CreateAsync();
        var sitePol = new Policy { Name = "Site policy", Active = true }; var groupPol = new Policy { Name = "Group policy", Active = true };
        var c = new Client { Name = "Acme" }; var s = new Site { Name = "Main", Client = c, Policy = sitePol };
        db.AddRange(sitePol, groupPol, c, s,
            new Agent { AgentId = A, Hostname = "PC-A", Site = s, LastSeen = DateTime.UtcNow },
            new Agent { AgentId = B, Hostname = "PC-B", Site = s },
            new Agent { AgentId = C, Hostname = "PC-C", Site = s });
        await db.SaveChangesAsync();
        var audit = new Audit();
        return (new AssetGroupService(db, new Perms(deny), audit), db, audit, sitePol, groupPol);
    }

    [PostgresFact]
    public async Task AGroupCanBeCreatedChangedAndDeletedAndNamesStayUnique()
    {
        var (svc, db, _, _, pol) = await SetupAsync(); await using var _ = db;
        var g = (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = "Reception", Description = "Front desk", Policy = pol.Id })).Value!;
        Assert.Equal("Group policy", g.PolicyName);
        Assert.Equal(OutcomeStatus.Conflict, (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = "Reception" })).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = " " })).Status);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = "X", Policy = 9999 })).Status);

        var changed = (await svc.UpdateAsync(Admin, g.Id, new AssetGroupUpsert { Name = "Reception PCs", ClearPolicy = true })).Value!;
        Assert.Equal("Reception PCs", changed.Name); Assert.Null(changed.PolicyId); Assert.Equal("Front desk", changed.Description);
        Assert.True((await svc.DeleteAsync(Admin, g.Id)).IsOk);
        Assert.Empty((await svc.ListAsync()).Value!);
    }

    [PostgresFact]
    public async Task SettingAPolicyOnAGroupNeedsThePolicyPermission()
    {
        var (svc, db, _, _, pol) = await SetupAsync(); await using var _ = db;
        var limited = new User { Id = 2, Username = "ops", Role = new Role { Name = "r", Permissions = [Perm.ManageGroups] } };
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.CreateAsync(limited, new AssetGroupUpsert { Name = "G", Policy = pol.Id })).Status);
        Assert.True((await svc.CreateAsync(limited, new AssetGroupUpsert { Name = "G" })).IsOk);     // without a policy it is fine
    }

    [PostgresFact]
    public async Task MembersCanBeAddedMovedAndRemovedAndAGroupHoldsEachAssetOnce()
    {
        var (svc, db, audit, _, _) = await SetupAsync(); await using var _ = db;
        var g1 = (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = "One" })).Value!;
        var g2 = (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = "Two" })).Value!;
        var added = (await svc.AddMembersAsync(Admin, g1.Id, new GroupMembersRequest { AgentIds = [A, B] })).Value!;
        Assert.Equal(2, added.Members); Assert.Equal(["PC-A", "PC-B"], added.Items.Select(i => i.Hostname).ToArray());
        Assert.True(added.Items.Single(i => i.Hostname == "PC-A").Online);

        var moved = (await svc.AddMembersAsync(Admin, g2.Id, new GroupMembersRequest { AgentIds = [B] })).Value!;
        Assert.Equal(1, moved.Members);
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await svc.GetAsync(Admin, g1.Id)).Value!.Members);          // B left the first group
        Assert.Contains(audit.Messages, m => m.Contains("1 moved from another group"));

        var removed = (await svc.RemoveMembersAsync(Admin, g1.Id, new GroupMembersRequest { AgentIds = [A] })).Value!;
        Assert.Equal(0, removed.Members);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.AddMembersAsync(Admin, g1.Id, new GroupMembersRequest { AgentIds = [] })).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.AddMembersAsync(Admin, g1.Id, new GroupMembersRequest { AgentIds = ["bad id"] })).Status);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.AddMembersAsync(Admin, g1.Id, new GroupMembersRequest { AgentIds = ["grp-agent-number-999999"] })).Status);
    }

    [PostgresFact]
    public async Task AnAssetTheCallerMayNotTouchCannotBeAdded()
    {
        var (svc, db, _, _, _) = await SetupAsync(deny: B); await using var _ = db;
        var g = (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = "G" })).Value!;
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.AddMembersAsync(Admin, g.Id, new GroupMembersRequest { AgentIds = [A, B] })).Status);
        Assert.Equal(0, (await svc.GetAsync(Admin, g.Id)).Value!.Members);
    }

    [PostgresFact]
    public async Task TheGroupPolicyBeatsTheSitePolicyButNotTheAssetsOwn()
    {
        var (svc, db, _, sitePol, groupPol) = await SetupAsync(); await using var _ = db;
        var g = (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = "G", Policy = groupPol.Id })).Value!;
        await svc.AddMembersAsync(Admin, g.Id, new GroupMembersRequest { AgentIds = [A] });
        var a = await db.Agents.SingleAsync(x => x.AgentId == A);
        var b = await db.Agents.SingleAsync(x => x.AgentId == B);

        Assert.Equal(groupPol.Id, await EffectivePolicy.IdForAsync(db, a));       // member: group policy
        Assert.Equal(sitePol.Id, await EffectivePolicy.IdForAsync(db, b));        // not a member: site policy
        var map = await EffectivePolicy.MapAsync(db);
        Assert.Equal(groupPol.Id, map[a.Id]); Assert.Equal(sitePol.Id, map[b.Id]);

        var own = new Policy { Name = "Own", Active = true }; db.Policies.Add(own); await db.SaveChangesAsync();
        a.PolicyId = own.Id; await db.SaveChangesAsync();
        Assert.Equal(own.Id, await EffectivePolicy.IdForAsync(db, a));            // the asset's own policy wins
        a.PolicyId = null; a.BlockPolicyInheritance = true; await db.SaveChangesAsync();
        Assert.Null(await EffectivePolicy.IdForAsync(db, a));                     // blocked: none

        a.BlockPolicyInheritance = false; groupPol.Active = false; await db.SaveChangesAsync();
        Assert.Equal(sitePol.Id, await EffectivePolicy.IdForAsync(db, a));        // inactive group policy: falls to the site
    }

    [PostgresFact]
    public async Task DeletingAGroupReleasesItsMembersAndDeletingItsPolicyClearsTheGroup()
    {
        var (svc, db, _, _, groupPol) = await SetupAsync(); await using var _ = db;
        var g = (await svc.CreateAsync(Admin, new AssetGroupUpsert { Name = "G", Policy = groupPol.Id })).Value!;
        await svc.AddMembersAsync(Admin, g.Id, new GroupMembersRequest { AgentIds = [A, B] });

        db.Policies.Remove(await db.Policies.SingleAsync(p => p.Id == groupPol.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Null((await svc.GetAsync(Admin, g.Id)).Value!.PolicyId);

        Assert.True((await svc.DeleteAsync(Admin, g.Id)).IsOk);
        db.ChangeTracker.Clear();
        Assert.All(await db.Agents.AsNoTracking().ToListAsync(), a => Assert.Null(a.GroupId));
    }
}
