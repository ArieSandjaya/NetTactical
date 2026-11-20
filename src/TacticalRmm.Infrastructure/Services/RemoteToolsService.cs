using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class RemoteToolsService(AppDbContext db, IAgentCommandBus bus, IAuditService audit) : IRemoteToolsService
{
    private async Task<(int Id, string Hostname, string Plat)?> FindAsync(string agentId, CancellationToken ct)
    {
        var a = await db.Agents.AsNoTracking().Where(x => x.AgentId == agentId).Select(x => new { x.Id, x.Hostname, x.Plat }).FirstOrDefaultAsync(ct);
        return a is null ? null : (a.Id, a.Hostname, a.Plat ?? "");
    }

    private static bool IsWindows(string plat) => string.Equals(plat, "windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>A failure the whole list call should report, or null when the reply may be read.</summary>
    private static Outcome<T>? Unreachable<T>(object? reply) => reply as string switch
    {
        "natsdown" => Outcome.Conflict<T>("The NATS server is not reachable, so the agent cannot be asked."),
        "timeout" => Outcome.Conflict<T>("The agent did not answer (it may be offline)."),
        "malformed" => Outcome.Invalid<T>("The agent sent a reply that could not be read."),
        _ => null,
    };

    public async Task<Outcome<IReadOnlyList<ServiceDto>>> ServicesAsync(string agentId, CancellationToken ct = default)
    {
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<IReadOnlyList<ServiceDto>>("Agent not found");
        if (!IsWindows(a.Plat)) return Outcome.Invalid<IReadOnlyList<ServiceDto>>("Services are only available on Windows assets.");
        var reply = await bus.RequestAsync(agentId, new Dictionary<string, object?> { ["func"] = "winservices" }, TimeSpan.FromSeconds(20), ct);
        if (Unreachable<IReadOnlyList<ServiceDto>>(reply) is { } bad) return bad;
        var list = RemoteTools.ServicesFromReply(reply);
        return list is null ? Outcome.Invalid<IReadOnlyList<ServiceDto>>("The agent sent an unexpected reply (not a list of services).") : Outcome.Ok<IReadOnlyList<ServiceDto>>(list);
    }

    public async Task<Outcome<ToolResult>> ServiceActionAsync(User caller, string agentId, ServiceActionRequest req, CancellationToken ct = default)
    {
        var action = req.Action?.Trim().ToLowerInvariant();
        if (!RemoteTools.ServiceActions.Contains(action)) return Outcome.Invalid<ToolResult>("action must be start, stop, restart, disable or enable");
        if (!RemoteTools.ValidServiceName(req.Name)) return Outcome.Invalid<ToolResult>("A service name is required.");
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<ToolResult>("Agent not found");
        if (!IsWindows(a.Plat)) return Outcome.Invalid<ToolResult>("Services are only available on Windows assets.");

        await audit.LogAsync(caller.Username, AuditAction.ExecCommand, "agent", $"{caller.Username} ran '{action}' on service {req.Name} of {a.Hostname}", a.Hostname, a.Id, ct: ct);
        var reply = await bus.RequestAsync(agentId, RemoteTools.ServiceCommand(req.Name!.Trim(), action!), TimeSpan.FromSeconds(45), ct);
        var (ok, msg) = RemoteTools.ServiceActionResult(reply);
        return ok ? Outcome.Ok(new ToolResult(msg)) : Outcome.Conflict<ToolResult>(msg);
    }

    public async Task<Outcome<IReadOnlyList<ProcessDto>>> ProcessesAsync(string agentId, CancellationToken ct = default)
    {
        if (await FindAsync(agentId, ct) is null) return Outcome.NotFound<IReadOnlyList<ProcessDto>>("Agent not found");
        var reply = await bus.RequestAsync(agentId, new Dictionary<string, object?> { ["func"] = "procs" }, TimeSpan.FromSeconds(20), ct);
        if (Unreachable<IReadOnlyList<ProcessDto>>(reply) is { } bad) return bad;
        var list = RemoteTools.ProcessesFromReply(reply);
        return list is null ? Outcome.Invalid<IReadOnlyList<ProcessDto>>("The agent sent an unexpected reply (not a list of processes).") : Outcome.Ok<IReadOnlyList<ProcessDto>>(list);
    }

    public async Task<Outcome<ToolResult>> KillAsync(User caller, string agentId, KillRequest req, CancellationToken ct = default)
    {
        if (!RemoteTools.ValidPid(req.Pid)) return Outcome.Invalid<ToolResult>("That process cannot be ended.");
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<ToolResult>("Agent not found");

        await audit.LogAsync(caller.Username, AuditAction.ExecCommand, "agent", $"{caller.Username} ended process {req.Pid}{(string.IsNullOrWhiteSpace(req.Name) ? "" : $" ({req.Name})")} on {a.Hostname}", a.Hostname, a.Id, ct: ct);
        var reply = await bus.RequestAsync(agentId, new Dictionary<string, object?> { ["func"] = "killproc", ["procpid"] = req.Pid }, TimeSpan.FromSeconds(20), ct);
        var (ok, msg) = RemoteTools.KillResult(reply);
        return ok ? Outcome.Ok(new ToolResult(msg)) : Outcome.Conflict<ToolResult>(msg);
    }
}
