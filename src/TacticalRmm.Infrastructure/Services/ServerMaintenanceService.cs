using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class ServerMaintenanceService(AppDbContext db, IAgentService agents, IAuditService audit) : IServerMaintenanceService
{
    private IQueryable<AuditLog> Audits(DateTime c) => db.AuditLogs.Where(x => x.EntryTime < c);
    private IQueryable<Alert> Alerts(DateTime c) => db.Alerts.Where(x => x.Resolved && x.ResolvedOn != null && x.ResolvedOn < c);
    private IQueryable<AgentHistory> History(DateTime c) => db.AgentHistory.Where(x => x.Time < c);
    private IQueryable<UserToken> Tokens(DateTime c) => db.UserTokens.Where(x => x.Expiry != null && x.Expiry < c);
    private IQueryable<Agent> Stale(DateTime c) => db.Agents.Where(x => x.LastSeen != null && x.LastSeen < c);

    private async Task<int> CountAsync(string action, DateTime c, CancellationToken ct) => action switch
    {
        ServerMaintenance.PruneAudit => await Audits(c).CountAsync(ct),
        ServerMaintenance.PruneAlerts => await Alerts(c).CountAsync(ct),
        ServerMaintenance.PruneHistory => await History(c).CountAsync(ct),
        ServerMaintenance.PruneTokens => await Tokens(c).CountAsync(ct),
        _ => await Stale(c).CountAsync(ct),
    };

    public async Task<Outcome<MaintenanceResult>> PreviewAsync(string? action, int days, CancellationToken ct = default)
    {
        if (ServerMaintenance.Validate(action, days) is { } bad) return Outcome.Invalid<MaintenanceResult>(bad);
        var n = await CountAsync(action!, ServerMaintenance.Cutoff(days, DateTime.UtcNow), ct);
        return Outcome.Ok(new MaintenanceResult(action!, days, n, false));
    }

    public async Task<Outcome<MaintenanceResult>> RunAsync(User caller, string? action, int days, CancellationToken ct = default)
    {
        if (ServerMaintenance.Validate(action, days) is { } bad) return Outcome.Invalid<MaintenanceResult>(bad);
        var c = ServerMaintenance.Cutoff(days, DateTime.UtcNow);
        int n;
        switch (action)
        {
            case ServerMaintenance.PruneAudit: n = await Audits(c).ExecuteDeleteAsync(ct); break;
            case ServerMaintenance.PruneAlerts: n = await Alerts(c).ExecuteDeleteAsync(ct); break;
            case ServerMaintenance.PruneHistory: n = await History(c).ExecuteDeleteAsync(ct); break;
            case ServerMaintenance.PruneTokens: n = await Tokens(c).ExecuteDeleteAsync(ct); break;
            default:
                // Agents go one by one (their related rows and NATS state are cleaned by the agent service), a batch at a time.
                var ids = await Stale(c).OrderBy(a => a.LastSeen).Select(a => a.AgentId).Take(ServerMaintenance.MaxStaleAgents).ToListAsync(ct);
                foreach (var id in ids) await agents.DeleteAsync(id, caller.Username, uninstall: false, ct);
                n = ids.Count;
                break;
        }
        await audit.LogAsync(caller.Username, AuditAction.Delete, "server", $"{caller.Username} ran server maintenance {action} (older than {days} days): {n} removed", ct: ct);
        return Outcome.Ok(new MaintenanceResult(action!, days, n, true));
    }
}
