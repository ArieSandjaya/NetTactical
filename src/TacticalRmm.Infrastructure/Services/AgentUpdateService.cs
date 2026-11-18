using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class AgentUpdateService(AppDbContext db, IAgentCommandBus bus, IAuditService audit, IOptions<TacticalOptions> opt) : IAgentUpdateService
{
    public async Task<Outcome<AgentUpdateDto>> UpdateAsync(string username, string agentId, CancellationToken ct = default)
    {
        var a = await db.Agents.AsNoTracking().Where(x => x.AgentId == agentId).Select(x => new { x.Hostname, x.Plat, x.Goarch, x.Version }).FirstOrDefaultAsync(ct);
        if (a is null) return Outcome.NotFound<AgentUpdateDto>("Agent not found");
        var latest = opt.Value.LatestAgentVer;
        var plan = AgentUpdatePlan.Decide(a.Plat, a.Goarch, a.Version, latest);
        if (plan.Error is { } e) return Outcome.Invalid<AgentUpdateDto>(e);
        if (plan.Skip is { } s) return Outcome.Ok(new AgentUpdateDto("skipped", s));
        var pong = await bus.RequestAsync(agentId, new Dictionary<string, object?> { ["func"] = "ping" }, TimeSpan.FromSeconds(3), ct);
        if (pong as string == "natsdown") return Outcome.Conflict<AgentUpdateDto>("The NATS server is not reachable.");
        if (pong as string != "pong") return Outcome.Conflict<AgentUpdateDto>("The agent did not answer (it may be offline).");
        await bus.PublishAsync(agentId, plan.Payload!, ct);
        await audit.LogAsync(username, AuditAction.Modify, "agent", $"{username} started an agent update of {a.Hostname} from {a.Version} to {latest}", a.Hostname, null, null, ct);
        return Outcome.Ok(new AgentUpdateDto("started", $"Update to {latest} started. The agent restarts and reports the new version in a minute or two."));
    }
}
