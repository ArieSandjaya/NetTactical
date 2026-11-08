using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class AgentService(
    AppDbContext db, IAgentCommandBus bus, ITokenService tokens, IPasswordHasher hasher,
    IAuditService audit, INatsConfigWriter natsConf, IOptions<TacticalOptions> opt) : IAgentService
{
    public static string StatusOf(Agent a, DateTime now)
    {
        if (a.LastSeen is null) return "offline";
        var offline = now.AddMinutes(-a.OfflineTime);
        var overdue = now.AddMinutes(-a.OverdueTime);
        if (a.LastSeen >= offline) return "online";
        return a.LastSeen < overdue ? "overdue" : "offline";
    }

    public async Task<IReadOnlyList<AgentListItem>> ListAsync(User user, CancellationToken ct = default)
    {
        var q = db.Agents.AsNoTracking().Include(a => a.Site).ThenInclude(s => s.Client).AsQueryable();
        if (!PermissionEvaluator.IsSuper(user) && user.Role is { } r && (r.CanViewClients.Count > 0 || r.CanViewSites.Count > 0))
        {
            var cids = r.CanViewClients.Select(c => c.Id).ToList();
            var sids = r.CanViewSites.Select(s => s.Id).ToList();
            q = q.Where(a => sids.Contains(a.SiteId) || cids.Contains(a.Site.ClientId));
        }
        var now = DateTime.UtcNow;
        var list = await q.OrderBy(a => a.Hostname).ToListAsync(ct);
        return list.Select(a => new AgentListItem(a.Id, a.AgentId, a.Hostname, a.Plat, a.OperatingSystem, a.Version,
            a.LastSeen, a.PublicIp, a.MonitoringType, a.Site.Name, a.Site.Client.Name, a.MaintenanceMode,
            a.NeedsReboot, a.LoggedInUsername, StatusOf(a, now))).ToList();
    }

    public Task<Agent?> GetByAgentIdAsync(string agentId, CancellationToken ct = default) =>
        db.Agents.Include(a => a.Site).ThenInclude(s => s.Client).FirstOrDefaultAsync(a => a.AgentId == agentId, ct);

    public Task<Agent?> GetByUserAsync(int userId, CancellationToken ct = default) =>
        db.Agents.Include(a => a.Site).ThenInclude(s => s.Client)
          .FirstOrDefaultAsync(a => db.Users.Any(u => u.Id == userId && u.AgentId == a.Id), ct);

    public async Task<NewAgentResult> RegisterAsync(NewAgentRequest req, string installerUser, string? ip, CancellationToken ct = default)
    {
        if (!AgentIdValidator.IsValid(req.AgentId)) throw new ArgumentException("Invalid agent_id");
        if (!await db.Sites.AnyAsync(s => s.Id == req.Site, ct)) throw new ArgumentException("Invalid site");
        if (await db.Agents.AnyAsync(a => a.AgentId == req.AgentId, ct))
            throw new InvalidOperationException("Agent already exists. Remove old agent first if trying to re-install");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var agent = new Agent
        {
            AgentId = req.AgentId, Hostname = req.Hostname, SiteId = req.Site, MonitoringType = req.MonitoringType,
            Description = req.Description, MeshNodeId = req.MeshNodeId, Goarch = req.Goarch, Plat = req.Plat,
            LastSeen = DateTime.UtcNow
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);

        var user = new User
        {
            Username = req.AgentId, AgentId = agent.Id,
            PasswordHash = hasher.Hash(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(30)))
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var token = await tokens.CreateAgentTokenAsync(user.Id, ct);
        await tx.CommitAsync(ct);

        await audit.LogAsync(installerUser, AuditAction.AgentInstall, "agent", $"{installerUser} installed new agent {agent.Hostname}",
            agent.Hostname, agent.Id, ip, ct);
        await natsConf.ReloadAsync(ct); // a new agent must be registered in nats-rmm.conf
        return new NewAgentResult(agent.Id, token);
    }

    public async Task DeleteAsync(string agentId, string username, CancellationToken ct = default)
    {
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.AgentId == agentId, ct) ?? throw new KeyNotFoundException();
        var user = await db.Users.FirstOrDefaultAsync(u => u.AgentId == agent.Id, ct);
        if (user is not null)
        {
            db.AgentTokens.RemoveRange(db.AgentTokens.Where(t => t.UserId == user.Id));
            db.Users.Remove(user);
        }
        db.Agents.Remove(agent);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(username, AuditAction.Delete, "agent", $"{username} deleted agent {agent.Hostname}", agent.Hostname, null, null, ct);
        await natsConf.ReloadAsync(ct);
    }

    public async Task<object?> RunScriptAsync(string agentId, RunScriptRequest req, string username, CancellationToken ct = default)
    {
        var agent = await GetByAgentIdAsync(agentId, ct) ?? throw new KeyNotFoundException("agent");
        var script = await db.Scripts.FindAsync([req.Script], ct) ?? throw new KeyNotFoundException("script");
        var runAsUser = script.RunAsUser || req.RunAsUser;
        var timeout = req.Timeout + 3;

        await audit.LogAsync(username, AuditAction.ExecScript, "agent", $"{username} ran script {script.Name} on {agent.Hostname}", agent.Hostname, agent.Id, ct: ct);
        var hist = new AgentHistory { AgentId = agent.Id, Time = DateTime.UtcNow, Type = "script_run", Command = script.Name, Username = username[..Math.Min(50, username.Length)] };
        db.AgentHistory.Add(hist);
        await db.SaveChangesAsync(ct);

        var data = new Dictionary<string, object?>
        {
            ["func"] = "runscript",
            ["timeout"] = timeout,
            ["script_args"] = ScriptArgParser.Parse(agent, script.Shell, req.Args ?? script.Args),
            ["payload"] = new Dictionary<string, object?> { ["code"] = script.ScriptBody, ["shell"] = script.Shell },
            ["run_as_user"] = runAsUser,
            ["env_vars"] = ScriptArgParser.Parse(agent, script.Shell, req.EnvVars ?? script.EnvVars),
            ["nushell_enable_config"] = opt.Value.Agent.NushellEnableConfig,
            ["deno_default_permissions"] = opt.Value.Agent.DenoDefaultPermissions,
            ["id"] = hist.Id,
        };

        switch (req.Output)
        {
            case "wait":
            {
                var r = await bus.RequestAsync(agent.AgentId, data, TimeSpan.FromSeconds(timeout), ct);
                hist.ScriptResults = JsonSerializer.Serialize(r);
                await db.SaveChangesAsync(ct);
                return r;
            }
            case "forget":
                await bus.PublishAsync(agent.AgentId, data, ct);
                return "ok";
            default:
                throw new NotSupportedException($"output '{req.Output}' is not implemented yet (only wait/forget)");
        }
    }
}
