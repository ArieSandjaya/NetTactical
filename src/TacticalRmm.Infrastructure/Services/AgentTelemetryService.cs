using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class AgentTelemetryService(AppDbContext db) : IAgentTelemetryService
{
    public async Task SetMeshNodeIdAsync(Agent agent, string nodeId, CancellationToken ct = default)
    {
        await db.Agents.Where(a => a.Id == agent.Id && a.MeshNodeId != nodeId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.MeshNodeId, nodeId), ct);
    }

    public Task SetChocoInstalledAsync(Agent agent, bool installed, CancellationToken ct = default) =>
        db.Agents.Where(a => a.Id == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ChocoInstalled, installed), ct);

    public async Task SaveSoftwareAsync(Agent agent, string softwareJson, CancellationToken ct = default)
    {
        var row = await db.InstalledSoftware.FirstOrDefaultAsync(x => x.AgentId == agent.Id, ct);
        if (row is null) db.InstalledSoftware.Add(new InstalledSoftware { AgentId = agent.Id, Software = softwareJson });
        else row.Software = softwareJson;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> SaveHistoryResultAsync(Agent agent, long historyId, string scriptResultsJson, string? stdout, int userId, CancellationToken ct = default)
    {
        // riwayat harus milik agent yang sedang autentikasi (filter agent__user di Django)
        var hist = await db.AgentHistory.FirstOrDefaultAsync(h => h.Id == historyId && h.AgentId == agent.Id, ct);
        if (hist is null) return false;

        hist.ScriptResults = scriptResultsJson;
        if (hist.SaveToAgentNote)
            db.AgentNotes.Add(new AgentNote { AgentId = agent.Id, UserId = userId, Note = stdout, EntryTime = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
