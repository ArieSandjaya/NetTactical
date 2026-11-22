using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using Xunit;

namespace TacticalRmm.Tests;

public class PermissionUpgradePostgresTests
{
    [PostgresFact]
    public async Task ExistingRolesGetTheNewPermissionsOnceFromTheOldOnes()
    {
        await using var db = await PgDb.CreateAsync();
        db.Roles.AddRange(
            new Role { Name = "ops", Permissions = ["can_send_cmd", "can_manage_agents", "can_list_agents", "can_edit_agent"] },
            new Role { Name = "viewer", Permissions = ["can_list_agents"] },
            new Role { Name = "empty", Permissions = [] },
            new Role { Name = "clients", Permissions = ["can_list_clients"] });
        await db.SaveChangesAsync();

        await SchemaUpgrade.RunAsync(db, NullLogger.Instance);
        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Name, r => r.Permissions);
        Assert.Superset(new HashSet<string> { "can_manage_procs", "can_use_mesh", "can_manage_software", "can_update_agents", "can_run_bulk", "can_list_notes", "can_manage_notes" }, roles["ops"].ToHashSet());
        Assert.Equal(["can_list_agents", "can_list_notes", "can_run_urlactions", "can_view_hardware"], roles["viewer"].Order());   // read-only permissions only
        Assert.Empty(roles["empty"]);
        Assert.Equal(["can_list_clients"], roles["clients"]);

        // an admin takes a permission away: a later start must not give it back
        db.ChangeTracker.Clear();   // the SQL upgrade changed rows behind the tracked copies
        var ops = await db.Roles.SingleAsync(r => r.Name == "ops");
        ops.Permissions = ops.Permissions.Where(p => p != "can_use_mesh").ToList();
        await db.SaveChangesAsync();
        await SchemaUpgrade.RunAsync(db, NullLogger.Instance);
        db.ChangeTracker.Clear();
        Assert.DoesNotContain("can_use_mesh", (await db.Roles.AsNoTracking().SingleAsync(r => r.Name == "ops")).Permissions);
    }
}

public class PermissionUpgradeV12PostgresTests
{
    [PostgresFact]
    public async Task EventLogsUrlActionsAndDeploymentsFollowTheOldPermissions()
    {
        await using var db = await PgDb.CreateAsync();
        db.Roles.AddRange(new Role { Name = "ops", Permissions = ["can_send_cmd", "can_list_agents", "can_install_agents"] }, new Role { Name = "none", Permissions = ["can_list_clients"] });
        await db.SaveChangesAsync();
        await SchemaUpgrade.RunAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Name, r => r.Permissions);
        Assert.Superset(new HashSet<string> { "can_view_eventlogs", "can_run_urlactions", "can_list_deployments", "can_manage_deployments" }, roles["ops"].ToHashSet());
        Assert.DoesNotContain("can_do_server_maint", roles["ops"]);          // administrators only until granted
        Assert.DoesNotContain("can_edit_core_settings", roles["ops"]);
        Assert.Equal(["can_list_clients"], roles["none"]);
    }
}
