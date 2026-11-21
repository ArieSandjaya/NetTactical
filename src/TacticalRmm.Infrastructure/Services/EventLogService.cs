using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class EventLogService(AppDbContext db, IAgentCommandBus bus) : IEventLogService
{
    public async Task<Outcome<EventLogDto>> ReadAsync(string agentId, string? log, string? level, int hours, int max, string? text, int? eventId, CancellationToken ct = default)
    {
        if (EventLogs.Validate(log, level, hours, max, text, eventId) is { } bad) return Outcome.Invalid<EventLogDto>(bad);
        var plat = await db.Agents.AsNoTracking().Where(a => a.AgentId == agentId).Select(a => a.Plat).FirstOrDefaultAsync(ct);
        var exists = await db.Agents.AnyAsync(a => a.AgentId == agentId, ct);
        if (!exists) return Outcome.NotFound<EventLogDto>("Agent not found");
        if (!string.Equals(plat, "windows", StringComparison.OrdinalIgnoreCase)) return Outcome.Invalid<EventLogDto>("The event log is only available for Windows assets.");
        var canon = EventLogs.CanonicalLog(log!);
        var (payload, err) = await AgentScripts.RunAsync(bus, agentId, EventLogs.Script(canon, level!, hours, max, text, eventId), TimeSpan.FromSeconds(90), ct);
        if (err is not null) return err.Contains("did not answer") || err.Contains("NATS") ? Outcome.Conflict<EventLogDto>(err) : Outcome.Invalid<EventLogDto>(err);
        return EventLogs.Parse(canon, max, payload!) is { } r ? Outcome.Ok(r) : Outcome.Invalid<EventLogDto>("The agent sent an event list that could not be read.");
    }
}
