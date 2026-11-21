using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class ServerMaintenancePostgresTests
{
    private sealed class Audit : IAuditService
    {
        public readonly List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }
    private sealed class Agents : IAgentService
    {
        public readonly List<string> Deleted = [];
        public Task DeleteAsync(string agentId, string username, bool uninstall = true, CancellationToken ct = default) { Deleted.Add(agentId + (uninstall ? ":u" : "")); return Task.CompletedTask; }
        public Task<IReadOnlyList<AgentListItem>> ListAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Agent?> GetByAgentIdAsync(string agentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Agent?> GetByUserAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NewAgentResult> RegisterAsync(NewAgentRequest req, string installerUser, string? ip, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<object?> RunScriptAsync(string agentId, RunScriptRequest req, string username, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly User Admin = new() { Id = 1, Username = "admin", IsSuperuser = true };

    [PostgresFact]
    public async Task Prunes_only_old_rows_and_audits_itself()
    {
        await using var db = await PgDb.CreateAsync();
        var now = DateTime.UtcNow;
        db.AuditLogs.AddRange(new AuditLog { Username = "a", EntryTime = now.AddDays(-100), Action = "x", ObjectType = "y" }, new AuditLog { Username = "a", EntryTime = now.AddDays(-5), Action = "x", ObjectType = "y" });
        db.Alerts.AddRange(new Alert { AlertTime = now.AddDays(-40), Resolved = true, ResolvedOn = now.AddDays(-30) }, new Alert { AlertTime = now.AddDays(-40) }, new Alert { AlertTime = now, Resolved = true, ResolvedOn = now });
        await db.SaveChangesAsync();
        var audit = new Audit(); var svc = new ServerMaintenanceService(db, new Agents(), audit);

        Assert.Equal(1, (await svc.PreviewAsync("prune_audit", 30)).Value!.Count);
        Assert.Equal(1, db.AuditLogs.Count(x => x.EntryTime < now.AddDays(-30)));
        var r = await svc.RunAsync(Admin, "prune_audit", 30);
        Assert.True(r.IsOk); Assert.Equal(1, r.Value!.Count); Assert.True(r.Value.Done);
        Assert.Equal(1, db.AuditLogs.Count());

        var a = await svc.RunAsync(Admin, "prune_alerts", 7);
        Assert.Equal(1, a.Value!.Count);
        Assert.Equal(2, db.Alerts.Count()); // the open alert and the freshly resolved one stay
        Assert.Equal(2, audit.Messages.Count);
    }

    [PostgresFact]
    public async Task Removes_only_assets_that_stopped_reporting_without_uninstalling()
    {
        await using var db = await PgDb.CreateAsync();
        var c = new Client { Name = "One" }; var s = new Site { Name = "S", Client = c };
        var now = DateTime.UtcNow;
        db.AddRange(c, s,
            new Agent { AgentId = "old-agent-number-000001", Hostname = "OLD", Site = s, LastSeen = now.AddDays(-90), Plat = "windows", Goarch = "amd64" },
            new Agent { AgentId = "new-agent-number-000001", Hostname = "NEW", Site = s, LastSeen = now, Plat = "windows", Goarch = "amd64" },
            new Agent { AgentId = "nvr-agent-number-000001", Hostname = "NEVER", Site = s, LastSeen = null, Plat = "windows", Goarch = "amd64" });
        await db.SaveChangesAsync();
        var agents = new Agents(); var svc = new ServerMaintenanceService(db, agents, new Audit());
        var r = await svc.RunAsync(Admin, "stale_agents", 30);
        Assert.Equal(1, r.Value!.Count);
        Assert.Equal(["old-agent-number-000001"], agents.Deleted);
    }

    [PostgresFact]
    public async Task Refuses_a_window_below_the_minimum()
    {
        await using var db = await PgDb.CreateAsync();
        var svc = new ServerMaintenanceService(db, new Agents(), new Audit());
        Assert.Equal(TacticalRmm.Application.Dtos.OutcomeStatus.Invalid, (await svc.RunAsync(Admin, "prune_audit", 1)).Status);
    }
}
