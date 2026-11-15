using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Loads the alert template that applies to an agent (site, then client, then the default) and turns it into flags.</summary>
public static class AlertTemplateResolver
{
    public static async Task<AlertFlags?> FlagsAsync(AppDbContext db, Agent agent, CancellationToken ct = default)
    {
        var site = await db.Sites.AsNoTracking().Where(s => s.Id == agent.SiteId)
            .Select(s => new { s.AlertTemplateId, ClientTemplateId = s.Client.AlertTemplateId }).FirstOrDefaultAsync(ct);
        var defaultId = await db.AlertTemplates.AsNoTracking().Where(x => x.IsDefault).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        var ids = new[] { site?.AlertTemplateId, site?.ClientTemplateId, defaultId }
            .Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return null;
        var found = await db.AlertTemplates.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var picked = AlertTemplateRules.Pick(Ref(found, site?.AlertTemplateId), Ref(found, site?.ClientTemplateId), Ref(found, defaultId));
        if (picked is not { } pid) return null;
        var tpl = found[pid];
        return new AlertFlags(tpl.FlagAgentOverdue, tpl.FlagCheckFailures, tpl.FlagTaskFailures);
    }

    private static TemplateRef? Ref(Dictionary<int, AlertTemplate> found, int? id) =>
        id is { } i && found.TryGetValue(i, out var tpl) ? new TemplateRef(i, tpl.Enabled) : null;
}
