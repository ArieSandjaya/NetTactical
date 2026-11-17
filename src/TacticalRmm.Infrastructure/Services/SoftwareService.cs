using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class SoftwareService(AppDbContext db, IAgentCommandBus bus, IAuditService audit) : ISoftwareService
{
    public async Task<Outcome<SoftwareDto>> GetAsync(string agentId, CancellationToken ct = default)
    {
        var a = await db.Agents.AsNoTracking().FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (a is null) return Outcome.NotFound<SoftwareDto>("Agent not found");
        var row = await db.InstalledSoftware.AsNoTracking().FirstOrDefaultAsync(x => x.AgentId == a.Id, ct);
        return Outcome.Ok(row is null ? new SoftwareDto([], null) : new SoftwareDto(SoftwareList.ParseJson(row.Software), row.UpdatedAt));
    }

    public async Task<Outcome<SoftwareDto>> RefreshAsync(string agentId, string username, CancellationToken ct = default)
    {
        var a = await db.Agents.AsNoTracking().FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (a is null) return Outcome.NotFound<SoftwareDto>("Agent not found");
        var reply = await bus.RequestAsync(agentId, new Dictionary<string, object?> { ["func"] = "softwarelist" }, TimeSpan.FromSeconds(15), ct);
        if (reply as string == "natsdown") return Outcome.Conflict<SoftwareDto>("The NATS server is not reachable, so the agent cannot be asked.");
        if (reply as string == "timeout") return Outcome.Conflict<SoftwareDto>("The agent did not answer (it may be offline). The list shown is the last one it sent.");
        if (reply as string == "malformed") return Outcome.Invalid<SoftwareDto>("The agent sent a reply that could not be read.");
        var items = SoftwareList.FromReply(reply);
        if (items is null) return Outcome.Invalid<SoftwareDto>("The agent sent an unexpected reply (not a list of applications).");

        var now = DateTime.UtcNow;
        var json = SoftwareList.ToJson(items);
        var row = await db.InstalledSoftware.FirstOrDefaultAsync(x => x.AgentId == a.Id, ct);
        if (row is null) db.InstalledSoftware.Add(new TacticalRmm.Domain.Entities.InstalledSoftware { AgentId = a.Id, Software = json, UpdatedAt = now });
        else { row.Software = json; row.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(username, AuditAction.Modify, "agent", $"{username} refreshed the installed software list of {a.Hostname} ({items.Count} items)", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(new SoftwareDto(items, now));
    }
}
