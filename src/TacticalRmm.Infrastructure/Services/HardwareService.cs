using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class HardwareService(AppDbContext db, IAgentCommandBus bus, IAuditService audit) : IHardwareService
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static HardwareDto ToDto(Agent a, HardwareInfo? h)
    {
        var snap = HardwareHealth.Deserialize(h?.Data);
        var (risk, reasons) = HardwareHealth.Assess(snap, h?.WarrantyEnd, Today);
        return new HardwareDto(a.AgentId, a.Hostname, h?.CollectedAt, risk, reasons, snap, h?.WarrantyEnd, h?.WarrantyNote, HardwareHealth.VendorLink(snap?.Manufacturer, snap?.Serial));
    }

    public async Task<Outcome<HardwareDto>> GetAsync(string agentId, CancellationToken ct = default)
    {
        var a = await db.Agents.AsNoTracking().FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (a is null) return Outcome.NotFound<HardwareDto>("Agent not found");
        var h = await db.HardwareInfos.AsNoTracking().FirstOrDefaultAsync(x => x.AgentId == a.Id, ct);
        return Outcome.Ok(ToDto(a, h));
    }

    public async Task<Outcome<HardwareDto>> RefreshAsync(string agentId, CancellationToken ct = default)
    {
        var a = await db.Agents.FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (a is null) return Outcome.NotFound<HardwareDto>("Agent not found");
        if (!string.Equals(a.Plat, "windows", StringComparison.OrdinalIgnoreCase)) return Outcome.Invalid<HardwareDto>("The hardware check is only available for Windows assets.");
        var (payload, err) = await AgentScripts.RunAsync(bus, agentId, HardwareHealth.Script, TimeSpan.FromSeconds(60), ct);
        if (err is not null) return err.Contains("did not answer") || err.Contains("NATS") ? Outcome.Conflict<HardwareDto>(err) : Outcome.Invalid<HardwareDto>(err);
        if (HardwareHealth.Parse(payload!) is not { } snap) return Outcome.Invalid<HardwareDto>("The agent sent a hardware report that could not be read.");
        var h = await db.HardwareInfos.FirstOrDefaultAsync(x => x.AgentId == a.Id, ct);
        if (h is null) { h = new HardwareInfo { AgentId = a.Id }; db.HardwareInfos.Add(h); }
        h.Data = HardwareHealth.Serialize(snap); h.CollectedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Outcome.Ok(ToDto(a, h));
    }

    public async Task<Outcome<HardwareDto>> SetWarrantyAsync(User caller, string agentId, WarrantyRequest req, CancellationToken ct = default)
    {
        var a = await db.Agents.FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (a is null) return Outcome.NotFound<HardwareDto>("Agent not found");
        if (HardwareHealth.ValidateWarranty(req.EndDate, req.Note, Today, out var end) is { } bad) return Outcome.Invalid<HardwareDto>(bad);
        var h = await db.HardwareInfos.FirstOrDefaultAsync(x => x.AgentId == a.Id, ct);
        if (h is null) { h = new HardwareInfo { AgentId = a.Id }; db.HardwareInfos.Add(h); }
        h.WarrantyEnd = end; h.WarrantyNote = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "agent", $"{caller.Username} set the warranty of {a.Hostname} to {(end is { } e ? e.ToString("yyyy-MM-dd") : "none")}", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(ToDto(a, h));
    }

    public async Task<Outcome<IReadOnlyList<HardwareRowDto>>> ListAsync(User caller, CancellationToken ct = default)
    {
        var all = await db.HardwareInfos.AsNoTracking().Include(h => h.Agent).ThenInclude(a => a.Site).ThenInclude(s => s.Client).ToListAsync(ct);
        var rows = new List<(int Rank, HardwareRowDto Row)>();
        foreach (var h in all.Where(h => PermissionEvaluator.CanSeeSite(caller, h.Agent.Site)))
        {
            var snap = HardwareHealth.Deserialize(h.Data);
            var (risk, reasons) = HardwareHealth.Assess(snap, h.WarrantyEnd, Today);
            rows.Add((risk == HardwareHealth.High ? 0 : risk == HardwareHealth.Watch ? 1 : risk == HardwareHealth.Unknown ? 3 : 2,
                new HardwareRowDto(h.Agent.AgentId, h.Agent.Hostname, h.Agent.Site.Client.Name, h.Agent.Site.Name, snap?.Manufacturer, snap?.Model, snap?.Serial, risk, reasons, h.WarrantyEnd, h.CollectedAt)));
        }
        return Outcome.Ok<IReadOnlyList<HardwareRowDto>>(rows.OrderBy(r => r.Rank).ThenBy(r => r.Row.Hostname, StringComparer.OrdinalIgnoreCase).Select(r => r.Row).ToList());
    }
}
