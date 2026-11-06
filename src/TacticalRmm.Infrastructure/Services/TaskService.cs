using System.Globalization;
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

public sealed class TaskService(AppDbContext db, IAgentCommandBus bus, IAlertService alerts, IOptions<TacticalOptions> opt) : ITaskService
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public async Task<TaskRunnerResponse?> GetForAgentAsync(Agent agent, int taskId, CancellationToken ct = default)
    {
        var task = await db.AutomatedTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null) return null;

        var allowed = task.AgentId == agent.Id
            || (task.PolicyId is not null && task.PolicyId == agent.PolicyId && !agent.BlockPolicyInheritance
                && await db.Policies.AnyAsync(p => p.Id == task.PolicyId && p.Active, ct));
        if (!allowed) return null;

        var actions = new List<TaskActionDto>();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(task.Actions) ? "[]" : task.Actions);
        foreach (var a in doc.RootElement.EnumerateArray())
        {
            var type = a.GetProperty("type").GetString();
            var timeout = a.TryGetProperty("timeout", out var t) ? t.GetInt32() : 90;
            if (type == "cmd")
            {
                var shell = a.GetProperty("shell").GetString() ?? "cmd";
                actions.Add(new TaskActionDto
                {
                    Type = "cmd", Shell = shell, Timeout = timeout,
                    Command = ScriptArgParser.Parse(agent, shell, [a.GetProperty("command").GetString() ?? ""])[0]
                });
            }
            else if (type == "script")
            {
                var script = await db.Scripts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == a.GetProperty("script").GetInt32(), ct);
                if (script is null) continue; // script sudah dihapus: lewati
                List<string> Strs(string name) => a.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new();
                actions.Add(new TaskActionDto
                {
                    Type = "script", ScriptName = script.Name, Code = script.ScriptBody, Shell = script.Shell, Timeout = timeout,
                    ScriptArgs = ScriptArgParser.Parse(agent, script.Shell, Strs("script_args")),
                    RunAsUser = script.RunAsUser, EnvVars = ScriptArgParser.Parse(agent, script.Shell, Strs("env_vars")),
                    NushellEnableConfig = opt.Value.Agent.NushellEnableConfig,
                    DenoDefaultPermissions = opt.Value.Agent.DenoDefaultPermissions
                });
            }
        }
        return new TaskRunnerResponse(task.Id, task.ContinueOnError, task.Enabled, actions);
    }

    public async Task SaveResultAsync(Agent agent, int taskId, TaskResultPayload p, CancellationToken ct = default)
    {
        var task = await db.AutomatedTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct) ?? throw new KeyNotFoundException("task");
        var res = await db.TaskResults.FirstOrDefaultAsync(r => r.AgentId == agent.Id && r.TaskId == taskId, ct);
        if (res is null) { res = new TaskResult { AgentId = agent.Id, TaskId = taskId }; db.TaskResults.Add(res); }

        res.Retcode = p.Retcode; res.Stdout = p.Stdout; res.Stderr = p.Stderr;
        res.ExecutionTime = p.ExecutionTime ?? (p.Runtime ?? 0).ToString("F4", CultureInfo.InvariantCulture);
        res.LastRun = DateTime.UtcNow; res.RunStatus = TaskRunStatus.Completed;
        res.Status = res.Retcode != 0 ? CheckStatus.Failing : CheckStatus.Passing;

        db.AgentHistory.Add(new AgentHistory
        {
            AgentId = agent.Id, Time = DateTime.UtcNow, Type = "task_run", Command = task.Name,
            ScriptResults = JsonSerializer.Serialize(p)
        });
        await db.SaveChangesAsync(ct);

        if (res.Status == CheckStatus.Passing) await alerts.ResolveTaskAsync(agent, task, ct);
        else await alerts.RaiseTaskFailureAsync(agent, task, res, ct);
    }

    public Task RunNowAsync(Agent agent, int taskId, CancellationToken ct = default) =>
        bus.PublishAsync(agent.AgentId, new Dictionary<string, object?> { ["func"] = "runtask", ["taskpk"] = taskId }, ct);
}
