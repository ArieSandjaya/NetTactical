using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TacticalRmm.Api.Auth;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Api.Controllers;

[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("agents")]
public class AgentsController(IAgentService agents, IPermissionService perms, IAgentCommandBus bus, IAuditService audit) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await agents.ListAsync(HttpContext.GetUser(), ct));

    [HttpGet("{agentId}"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> Get(string agentId, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        var a = await agents.GetByAgentIdAsync(agentId, ct);
        return a is null ? NotFound() : Ok(new { a.Id, a.AgentId, a.Hostname, a.Plat, a.Version, a.OperatingSystem, a.LastSeen, a.PublicIp,
            a.MonitoringType, a.Description, a.MaintenanceMode, a.NeedsReboot, a.CheckInterval, a.OfflineTime, a.OverdueTime,
            Site = a.Site.Name, Client = a.Site.Client.Name });
    }

    [HttpDelete("{agentId}"), RequirePerm(Perm.UninstallAgents)]
    public async Task<IActionResult> Delete(string agentId, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        try { await agents.DeleteAsync(agentId, HttpContext.GetUser().Username, ct); return Ok("ok"); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("{agentId}/ping"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> Ping(string agentId, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        var r = await bus.RequestAsync(agentId, new Dictionary<string, object?> { ["func"] = "ping" }, TimeSpan.FromSeconds(2), ct);
        return Ok(new { status = r as string == "pong" ? "online" : "offline" });
    }

    [HttpPost("{agentId}/reboot"), RequirePerm(Perm.RebootAgents)]
    public async Task<IActionResult> Reboot(string agentId, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        var r = await bus.RequestAsync(agentId, new Dictionary<string, object?> { ["func"] = "rebootnow" }, TimeSpan.FromSeconds(10), ct);
        return r as string == "ok" ? Ok("ok") : Problem("Unable to contact the agent");
    }

    [HttpPost("{agentId}/runscript"), RequirePerm(Perm.RunScripts)]
    public async Task<IActionResult> RunScript(string agentId, RunScriptRequest req, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        try { return Ok(await agents.RunScriptAsync(agentId, req, HttpContext.GetUser().Username, ct)); }
        catch (KeyNotFoundException e) { return NotFound(e.Message); }
        catch (NotSupportedException e) { return BadRequest(e.Message); }
    }

    [HttpPost("{agentId}/cmd"), RequirePerm(Perm.RunCommands)]
    public async Task<IActionResult> Cmd(string agentId, RawCmdRequest req, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        var u = HttpContext.GetUser();
        await audit.LogAsync(u.Username, AuditAction.ExecCommand, "agent", $"{u.Username} ran command on {agentId}: {req.Cmd}", ct: ct);
        var r = await bus.RequestAsync(agentId, new Dictionary<string, object?>
            { ["func"] = "rawcmd", ["timeout"] = req.Timeout, ["payload"] = new Dictionary<string, object?> { ["command"] = req.Cmd, ["shell"] = req.Shell } },
            TimeSpan.FromSeconds(req.Timeout + 2), ct);
        return Ok(r);
    }
}
public record RawCmdRequest(string Cmd, string Shell, int Timeout = 30);

[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("clients")]
public class ClientsController(AppDbContext db, IAuditService audit) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListClients)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var u = HttpContext.GetUser();
        var clients = await db.Clients.AsNoTracking().Include(c => c.Sites).OrderBy(c => c.Name).ToListAsync(ct);
        var result = clients
            .Select(c => new { c.Id, c.Name, Sites = c.Sites.Where(s => PermissionEvaluator.CanSeeSite(u, s)).Select(s => new { s.Id, s.Name }).ToList() })
            .Where(c => c.Sites.Count > 0 || (c.Sites.Count == 0 && PermissionEvaluator.IsSuper(u)));
        return Ok(result);
    }

    [HttpPost, RequirePerm(Perm.ManageClients)]
    public async Task<IActionResult> Create(CreateClientRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Site)) return BadRequest("Name and site required");
        var c = new Client { Name = req.Name.Trim(), Sites = { new Site { Name = req.Site.Trim() } } };
        db.Clients.Add(c);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(HttpContext.GetUser().Username, AuditAction.Add, "client", $"Added client {c.Name}", ct: ct);
        return Ok(new { c.Id, c.Name });
    }
}
public record CreateClientRequest(string Name, string Site);

[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("scripts")]
public class ScriptsController(AppDbContext db, IAuditService audit) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListScripts)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.Scripts.AsNoTracking().Where(s => !s.Hidden).OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Description, s.Shell, s.DefaultTimeout, s.RunAsUser, s.SupportedPlatforms }).ToListAsync(ct));

    [HttpPost, RequirePerm(Perm.ManageScripts)]
    public async Task<IActionResult> Create(Script s, CancellationToken ct)
    {
        s.Id = 0;
        db.Scripts.Add(s);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(HttpContext.GetUser().Username, AuditAction.Add, "script", $"Added script {s.Name}", ct: ct);
        return Ok(new { s.Id });
    }

    [HttpDelete("{id:int}"), RequirePerm(Perm.ManageScripts)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var s = await db.Scripts.FindAsync([id], ct);
        if (s is null) return NotFound();
        db.Scripts.Remove(s);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(HttpContext.GetUser().Username, AuditAction.Delete, "script", $"Deleted script {s.Name}", ct: ct);
        return Ok("ok");
    }
}

[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("alerts")]
public class AlertsController(AppDbContext db) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListAlerts)]
    public async Task<IActionResult> List([FromQuery] bool resolved = false, CancellationToken ct = default)
    {
        var u = HttpContext.GetUser();
        var rows = await db.Alerts.AsNoTracking().Include(a => a.Agent!).ThenInclude(g => g.Site)
            .Where(a => a.Resolved == resolved && !a.Hidden).OrderByDescending(a => a.AlertTime).Take(500).ToListAsync(ct);
        return Ok(rows.Where(a => a.Agent is null || PermissionEvaluator.CanSeeSite(u, a.Agent.Site))
            .Select(a => new { a.Id, a.AlertType, a.Message, a.Severity, a.AlertTime, a.Resolved, a.ResolvedOn, a.Snoozed, Hostname = a.Agent?.Hostname }));
    }

    [HttpPut("{id:int}/resolve"), RequirePerm(Perm.ManageAlerts)]
    public async Task<IActionResult> Resolve(int id, CancellationToken ct)
    {
        var a = await db.Alerts.Include(x => x.Agent!).ThenInclude(g => g.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (a.Agent is not null && !PermissionEvaluator.CanSeeSite(HttpContext.GetUser(), a.Agent.Site)) return Forbid();
        a.Resolved = true; a.ResolvedOn = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok("ok");
    }
}

[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("logs")]
public class LogsController(AppDbContext db) : ControllerBase
{
    [HttpGet("audit"), RequirePerm(Perm.ViewAuditLog)]
    public async Task<IActionResult> Audit([FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        var q = db.AuditLogs.AsNoTracking().OrderByDescending(l => l.EntryTime);
        return Ok(new { total = await q.CountAsync(ct), results = await q.Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToListAsync(ct) });
    }
}
