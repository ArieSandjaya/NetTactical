using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class UrlActionService(AppDbContext db, IAuditService audit) : IUrlActionService
{
    private static UrlActionDto ToDto(UrlAction a) => new(a.Id, a.Name, a.Description, a.Pattern);

    public async Task<Outcome<IReadOnlyList<UrlActionDto>>> ListAsync(CancellationToken ct = default)
    {
        var all = await db.UrlActions.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
        return Outcome.Ok<IReadOnlyList<UrlActionDto>>(all.Select(ToDto).ToList());
    }

    public async Task<Outcome<UrlActionDto>> SaveAsync(User caller, int? id, UrlActionRequest req, CancellationToken ct = default)
    {
        if (UrlActions.Validate(req.Name, req.Pattern) is { } bad) return Outcome.Invalid<UrlActionDto>(bad);
        var name = req.Name.Trim();
        if (await db.UrlActions.AnyAsync(a => a.Name.ToLower() == name.ToLower() && a.Id != (id ?? 0), ct)) return Outcome.Conflict<UrlActionDto>("An action with this name already exists.");
        UrlAction a;
        if (id is { } i)
        {
            a = await db.UrlActions.FirstOrDefaultAsync(x => x.Id == i, ct) ?? null!;
            if (a is null) return Outcome.NotFound<UrlActionDto>("URL action not found");
        }
        else { a = new UrlAction { CreatedBy = caller.Username, CreatedAt = DateTime.UtcNow }; db.UrlActions.Add(a); }
        a.Name = name; a.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(); a.Pattern = req.Pattern.Trim();
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, id is null ? AuditAction.Add : AuditAction.Modify, "url_action", $"{caller.Username} {(id is null ? "created" : "changed")} the URL action {name}", ct: ct);
        return Outcome.Ok(ToDto(a));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var a = await db.UrlActions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Outcome.NotFound<bool>("URL action not found");
        db.UrlActions.Remove(a);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "url_action", $"{caller.Username} deleted the URL action {a.Name}", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<UrlActionRunDto>> RunAsync(User caller, int id, string agentId, CancellationToken ct = default)
    {
        var a = await db.UrlActions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Outcome.NotFound<UrlActionRunDto>("URL action not found");
        var ag = await db.Agents.AsNoTracking().Include(x => x.Site).ThenInclude(s => s.Client).FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (ag is null) return Outcome.NotFound<UrlActionRunDto>("Agent not found");
        var url = UrlActions.Render(a.Pattern, k => k switch
        {
            "agent.hostname" => ag.Hostname, "agent.agent_id" => ag.AgentId, "agent.public_ip" => ag.PublicIp, "agent.logged_in_user" => ag.LoggedInUsername, "agent.description" => ag.Description,
            "client.name" => ag.Site.Client.Name, "client.id" => ag.Site.ClientId.ToString(), "site.name" => ag.Site.Name, "site.id" => ag.SiteId.ToString(), _ => null,
        });
        await audit.LogAsync(caller.Username, AuditAction.View, "url_action", $"{caller.Username} opened the URL action {a.Name} for {ag.Hostname}", ag.Hostname, ag.Id, ct: ct);
        return Outcome.Ok(new UrlActionRunDto(url));
    }
}
