using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class CheckService(AppDbContext db, IAlertService alerts, ITaskService tasks, IOptions<TacticalOptions> opt) : ICheckService
{
    // Kunci identitas untuk aturan override: check milik agent mengalahkan check policy yang sama.
    private static string Identity(Check c) => $"{c.CheckType}|{c.Disk}|{c.Ip}|{c.SvcName}|{c.ScriptId}|{c.LogName}|{c.EventId}";

    public async Task<IReadOnlyList<Check>> GetChecksForAgentAsync(Agent agent, CancellationToken ct = default)
    {
        var own = await db.Checks.AsNoTracking().Include(c => c.Script).Where(c => c.AgentId == agent.Id).ToListAsync(ct);
        if (agent.BlockPolicyInheritance || agent.PolicyId is null) return own;

        var policy = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == agent.PolicyId && p.Active, ct);
        if (policy is null) return own;

        var fromPolicy = await db.Checks.AsNoTracking().Include(c => c.Script).Where(c => c.PolicyId == policy.Id).ToListAsync(ct);
        var ownKeys = own.Select(Identity).ToHashSet();
        var merged = new List<Check>(own);
        merged.AddRange(fromPolicy.Where(p => policy.Enforced || !ownKeys.Contains(Identity(p))));
        return merged;
    }

    public async Task<CheckRunnerResponse> GetRunnerAsync(Agent agent, bool dueOnly, CancellationToken ct = default)
    {
        var checks = await GetChecksForAgentAsync(agent, ct);
        var ids = checks.Select(c => c.Id).ToList();
        var results = await db.CheckResults.AsNoTracking().Where(r => r.AgentId == agent.Id && ids.Contains(r.CheckId))
                              .ToDictionaryAsync(r => r.CheckId, ct);
        var now = DateTime.UtcNow;
        var list = checks
            .Where(c => !dueOnly || CheckEvaluator.IsDue(c, results.GetValueOrDefault(c.Id), agent.CheckInterval, now))
            .Select(c => Map(c, agent)).ToList();

        var intervals = checks.Where(c => c.RunInterval > 0).Select(c => c.RunInterval).Append(agent.CheckInterval);
        return new CheckRunnerResponse(agent.Id, intervals.Min(), list);
    }

    private static CheckRunnerItem Map(Check c, Agent agent) => new()
    {
        Id = c.Id, Agent = c.AgentId, CheckType = c.CheckType, RunInterval = c.RunInterval, AlertSeverity = c.AlertSeverity,
        ErrorThreshold = c.ErrorThreshold, WarningThreshold = c.WarningThreshold, Disk = c.Disk, Ip = c.Ip,
        Script = c.Script is null ? null : new ScriptCheckDto(c.Script.ScriptBody, c.Script.Shell, c.Script.RunAsUser),
        ScriptArgs = c.CheckType == CheckType.Script ? ScriptArgParser.Parse(agent, c.Script?.Shell ?? "", c.ScriptArgs) : new(),
        EnvVars = c.CheckType == CheckType.Script ? ScriptArgParser.Parse(agent, c.Script?.Shell ?? "", c.EnvVars) : new(),
        Timeout = c.Timeout, SvcName = c.SvcName, PassIfStartPending = c.PassIfStartPending,
        PassIfSvcNotExist = c.PassIfSvcNotExist, RestartIfStopped = c.RestartIfStopped, LogName = c.LogName,
        EventId = c.EventId, EventType = c.EventType, FailWhen = c.FailWhen, SearchLastDays = c.SearchLastDays,
    };

    public async Task<string> HandleResultAsync(Agent agent, CheckResultPayload p, CancellationToken ct = default)
    {
        // Perbaikan keamanan vs Django: agent hanya boleh melapor untuk check yang memang berlaku baginya.
        var applicable = await GetChecksForAgentAsync(agent, ct);
        var check = applicable.FirstOrDefault(c => c.Id == p.Id);
        if (check is null) return "forbidden";

        var result = await db.CheckResults.FirstOrDefaultAsync(r => r.AgentId == agent.Id && r.CheckId == check.Id, ct);
        if (result is null)
        {
            result = new CheckResult { AgentId = agent.Id, CheckId = check.Id };
            db.CheckResults.Add(result);
        }

        var previousRun = result.LastRun;
        CheckEvaluator.Apply(check, result, p);
        RecordMetric(agent, check, p, previousRun);
        await db.SaveChangesAsync(ct);

        if (result.Status == CheckStatus.Failing)
        {
            if (CheckEvaluator.ShouldAlert(check, result)) await alerts.RaiseCheckFailureAsync(agent, check, result, ct);
            var linked = await db.AutomatedTasks.AsNoTracking()
                .Where(t => t.AssignedCheckId == check.Id && t.Enabled).Select(t => t.Id).ToListAsync(ct);
            foreach (var id in linked) await tasks.RunNowAsync(agent, id, ct);
        }
        else if (result.Status == CheckStatus.Passing)
        {
            await alerts.ResolveCheckAsync(agent, check, ct);
        }
        return "ok";
    }

    /// <summary>Simpan titik metrik historis (persen terpakai) untuk check cpu/memori/disk, dengan jeda minimum.</summary>
    private void RecordMetric(Agent agent, Check check, CheckResultPayload p, DateTime? previousRun)
    {
        var now = DateTime.UtcNow;
        if (previousRun is { } prev && (now - prev).TotalSeconds < opt.Value.MetricMinIntervalSeconds) return;
        var value = MetricRules.UsedPercent(check.CheckType, p);
        if (value is null) return;
        db.CheckMetrics.Add(new CheckMetric
        {
            AgentId = agent.Id, CheckId = check.Id, CheckType = check.CheckType, Time = now,
            Value = value.Value, Label = check.CheckType == CheckType.DiskSpace ? check.Disk : null
        });
    }
}
