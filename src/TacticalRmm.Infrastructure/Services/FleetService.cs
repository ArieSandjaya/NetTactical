using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Data untuk dashboard aset: ringkasan, detail perangkat (overview), dan deret waktu metrik.</summary>
public sealed class FleetService(AppDbContext db, ICheckService checks, IOptions<TacticalOptions> opt) : IFleetService
{
    private HealthWeights W => opt.Value.Health;

    private sealed record AgentRow(int Id, string AgentId, string Hostname, string? Os, string Version, DateTime? LastSeen, int OfflineTime,
        double? BootTime, bool NeedsReboot, string? Disks, int SiteId, string SiteName, int ClientId, string ClientName);

    private sealed record AlertRow(int Id, int AgentId, string Severity, string? Message, DateTime Time, bool Snoozed);

    // ------------------------------------------------------------------------------------------ summary
    public async Task<Outcome<FleetSummaryDto>> GetSummaryAsync(User caller, int? clientId, int? siteId, int days, int riskLimit, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        riskLimit = Math.Clamp(riskLimit, 1, 50);
        var now = DateTime.UtcNow;

        var rows = await db.Agents.AsNoTracking()
            .Where(a => (clientId == null || a.Site.ClientId == clientId) && (siteId == null || a.SiteId == siteId))
            .Select(a => new AgentRow(a.Id, a.AgentId, a.Hostname, a.OperatingSystem, a.Version, a.LastSeen, a.OfflineTime, a.BootTime, a.NeedsReboot,
                a.Disks, a.SiteId, a.Site.Name, a.Site.ClientId, a.Site.Client.Name))
            .ToListAsync(ct);
        rows = rows.Where(r => PermissionEvaluator.CanSeeSite(caller, new Site { Id = r.SiteId, ClientId = r.ClientId })).ToList();

        var ids = rows.Select(r => r.Id).ToList();
        var inputs = await BuildInputsAsync(rows, ids, now, ct);
        var scored = rows.Select(r => (Row: r, Online: AgentDataParser.IsOnline(r.LastSeen, r.OfflineTime, now), Res: HealthScorer.Score(inputs[r.Id], W))).ToList();

        var openAlerts = ids.Count == 0 ? new List<AlertRow>() : await db.Alerts.AsNoTracking()
            .Where(a => a.AgentId != null && ids.Contains(a.AgentId.Value) && !a.Resolved && !a.Hidden)
            .Select(a => new AlertRow(a.Id, a.AgentId!.Value, a.Severity, a.Message, a.AlertTime, a.Snoozed)).ToListAsync(ct);

        var pending = ids.Count == 0 ? new Dictionary<int, int>() : await db.WinUpdates.AsNoTracking()
            .Where(u => ids.Contains(u.AgentId) && !u.Installed && u.Action != "ignore")
            .GroupBy(u => u.AgentId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);

        var avg = scored.Count == 0 ? 0 : (int)Math.Round(scored.Average(s => s.Res.Score));
        var atRiskCount = scored.Count(s => s.Res.Level != HealthScorer.Healthy && s.Res.Score < 70);
        var totals = new FleetTotals(scored.Count, scored.Count(s => s.Online), scored.Count(s => !s.Online), avg, HealthScorer.LevelOf(avg, W), atRiskCount);

        var alertSummary = new FleetAlerts(
            openAlerts.Count(a => a.Severity == AlertSeverity.Error), openAlerts.Count(a => a.Severity == AlertSeverity.Warning),
            openAlerts.Count(a => a.Severity == AlertSeverity.Info));

        var patch = new FleetPatch(
            rows.Count(r => !pending.ContainsKey(r.Id)),
            rows.Count(r => pending.TryGetValue(r.Id, out var n) && n is >= 1 and <= 4),
            rows.Count(r => pending.TryGetValue(r.Id, out var n) && n >= 5),
            pending.Count, rows.Count(r => r.NeedsReboot));

        var clientRows = scored.GroupBy(s => (s.Row.ClientId, s.Row.ClientName))
            .Select(g => new FleetClientRow(g.Key.ClientId, g.Key.ClientName, g.Count(), (int)Math.Round(g.Average(x => x.Res.Score)), g.Count(x => x.Res.Score < 70)))
            .OrderBy(c => c.AvgHealth).ThenBy(c => c.Name).ToList();

        var risk = scored.Where(s => s.Res.Score < 70).OrderBy(s => s.Res.Score).ThenBy(s => s.Row.Hostname).Take(riskLimit)
            .Select(s => new FleetRiskRow(s.Row.AgentId, s.Row.Hostname, s.Row.ClientName, s.Row.SiteName, s.Row.Os ?? "", s.Res.Score, s.Res.Level,
                s.Res.Factors.Take(4).Select(f => new HealthFactorDto(f.Code, f.Penalty, f.Detail)).ToList(),
                inputs[s.Row.Id].UptimeDays, s.Online)).ToList();

        var byId = rows.ToDictionary(r => r.Id);
        var recent = openAlerts.OrderByDescending(a => a.Time).Take(10)
            .Select(a => new FleetAlertRow(a.Id, byId[a.AgentId].AgentId, byId[a.AgentId].Hostname, byId[a.AgentId].ClientName, a.Severity, a.Message, a.Time)).ToList();

        var os = rows.GroupBy(r => string.IsNullOrWhiteSpace(r.Os) ? "Unknown" : r.Os!).Select(g => new CountRow(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Name).ToList();

        var latest = opt.Value.LatestAgentVer;
        var outdated = rows.Count(r => AgentVersion.IsInstallerOutdated(r.Version, latest));
        var versions = new FleetAgentVersions(latest, rows.Count - outdated, outdated);

        var trends = await BuildTrendsAsync(ids, days, now, ct);

        var scopeName = siteId is not null ? rows.FirstOrDefault()?.SiteName ?? "Site"
            : clientId is not null ? rows.FirstOrDefault()?.ClientName ?? "Client" : "Semua client";
        return Outcome.Ok(new FleetSummaryDto(new FleetScope(clientId, siteId, scopeName), totals, alertSummary, patch, clientRows, risk, recent, os, versions, trends));
    }

    private async Task<Dictionary<int, HealthInput>> BuildInputsAsync(List<AgentRow> rows, List<int> ids, DateTime now, CancellationToken ct)
    {
        var result = new Dictionary<int, HealthInput>();
        if (ids.Count == 0) return result;

        // CPU/memori: rata-rata 3 titik terakhir dalam 30 menit terakhir.
        var cut = now.AddMinutes(-30);
        var samples = await db.CheckMetrics.AsNoTracking()
            .Where(m => ids.Contains(m.AgentId) && m.Time > cut && (m.CheckType == CheckType.CpuLoad || m.CheckType == CheckType.Memory))
            .Select(m => new { m.AgentId, m.CheckType, m.Time, m.Value }).ToListAsync(ct);
        double? Recent(int id, string type)
        {
            var v = samples.Where(s => s.AgentId == id && s.CheckType == type).OrderByDescending(s => s.Time).Take(3).Select(s => s.Value).ToList();
            return v.Count == 0 ? null : v.Average();
        }

        var alerts = await db.Alerts.AsNoTracking()
            .Where(a => a.AgentId != null && ids.Contains(a.AgentId.Value) && !a.Resolved && !a.Hidden && !a.Snoozed)
            .Select(a => new { Id = a.AgentId!.Value, a.Severity }).ToListAsync(ct);
        var pending = await db.WinUpdates.AsNoTracking()
            .Where(u => ids.Contains(u.AgentId) && !u.Installed && u.Action != "ignore")
            .GroupBy(u => u.AgentId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);

        foreach (var r in rows)
        {
            var online = AgentDataParser.IsOnline(r.LastSeen, r.OfflineTime, now);
            var disks = AgentDataParser.ParseDisks(r.Disks);
            result[r.Id] = new HealthInput(online, Recent(r.Id, CheckType.CpuLoad), Recent(r.Id, CheckType.Memory), AgentDataParser.WorstDiskUsed(disks),
                alerts.Count(a => a.Id == r.Id && a.Severity == AlertSeverity.Error), alerts.Count(a => a.Id == r.Id && a.Severity == AlertSeverity.Warning),
                pending.GetValueOrDefault(r.Id), r.NeedsReboot, AgentDataParser.UptimeDays(r.BootTime, now),
                AgentVersion.IsInstallerOutdated(r.Version, opt.Value.LatestAgentVer));
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<TrendPoint>>> BuildTrendsAsync(List<int> ids, int days, DateTime now, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<TrendPoint>>();
        foreach (var t in MetricRules.Types) result[MetricRules.ApiName(t)] = new List<TrendPoint>();
        if (ids.Count == 0) return result;

        var from = now.Date.AddDays(-(days - 1));
        var daily = await db.CheckMetrics.AsNoTracking()
            .Where(m => ids.Contains(m.AgentId) && m.Time >= from)
            .GroupBy(m => new { m.CheckType, Day = m.Time.Date })
            .Select(g => new { g.Key.CheckType, g.Key.Day, Avg = g.Average(x => x.Value), N = g.Count() })
            .ToListAsync(ct);
        foreach (var t in MetricRules.Types)
            result[MetricRules.ApiName(t)] = daily.Where(d => d.CheckType == t).OrderBy(d => d.Day)
                .Select(d => new TrendPoint(DateTime.SpecifyKind(d.Day, DateTimeKind.Utc), Math.Round(d.Avg, 1), d.N)).ToList();
        return result;
    }

    // ------------------------------------------------------------------------------------------ overview
    public async Task<Outcome<AgentOverviewDto>> GetOverviewAsync(User caller, string agentId, CancellationToken ct = default)
    {
        var agent = await db.Agents.AsNoTracking().Include(a => a.Site).ThenInclude(s => s.Client).FirstOrDefaultAsync(a => a.AgentId == agentId, ct);
        if (agent is null) return Outcome.NotFound<AgentOverviewDto>("Agent not found");
        if (!ScopeGuard.CanSeeAgent(caller, agent)) return Outcome.Forbidden<AgentOverviewDto>();

        var now = DateTime.UtcNow;
        var online = AgentDataParser.IsOnline(agent.LastSeen, agent.OfflineTime, now);
        var disks = AgentDataParser.ParseDisks(agent.Disks);
        var outdated = AgentVersion.IsInstallerOutdated(agent.Version, opt.Value.LatestAgentVer);
        var uptime = AgentDataParser.UptimeDays(agent.BootTime, now);

        var cut = now.AddMinutes(-30);
        var recent = await db.CheckMetrics.AsNoTracking().Where(m => m.AgentId == agent.Id && m.Time > cut && m.CheckType != CheckType.DiskSpace)
            .OrderByDescending(m => m.Time).Select(m => new { m.CheckType, m.Value }).Take(40).ToListAsync(ct);
        double? Avg3(string t) { var v = recent.Where(x => x.CheckType == t).Take(3).Select(x => x.Value).ToList(); return v.Count == 0 ? null : v.Average(); }
        var cpu = Avg3(CheckType.CpuLoad); var ram = Avg3(CheckType.Memory); var worst = AgentDataParser.WorstDiskUsed(disks);

        var openAlerts = await db.Alerts.AsNoTracking().Where(a => a.AgentId == agent.Id && !a.Resolved && !a.Hidden).ToListAsync(ct);
        var updates = await db.WinUpdates.AsNoTracking().Where(u => u.AgentId == agent.Id && (!u.Installed || u.Result == "failed"))
            .OrderBy(u => u.Severity).Take(100).ToListAsync(ct);
        var pendingCount = updates.Count(u => !u.Installed && u.Action != "ignore");

        var health = HealthScorer.Score(new HealthInput(online, cpu, ram, worst,
            openAlerts.Count(a => !a.Snoozed && a.Severity == AlertSeverity.Error), openAlerts.Count(a => !a.Snoozed && a.Severity == AlertSeverity.Warning),
            pendingCount, agent.NeedsReboot, uptime, outdated), W);

        var applicable = await checks.GetChecksForAgentAsync(agent, ct);
        var checkIds = applicable.Select(c => c.Id).ToList();
        var checkResults = await db.CheckResults.AsNoTracking().Where(r => r.AgentId == agent.Id && checkIds.Contains(r.CheckId)).ToDictionaryAsync(r => r.CheckId, ct);

        var policyActive = agent.PolicyId is not null && !agent.BlockPolicyInheritance && await db.Policies.AnyAsync(p => p.Id == agent.PolicyId && p.Active, ct);
        var tasks = await db.AutomatedTasks.AsNoTracking()
            .Where(t => !t.PendingDeletion && (t.AgentId == agent.Id || (policyActive && t.PolicyId == agent.PolicyId))).OrderBy(t => t.Name).ToListAsync(ct);
        var taskIds = tasks.Select(t => t.Id).ToList();
        var taskResults = await db.TaskResults.AsNoTracking().Where(r => r.AgentId == agent.Id && taskIds.Contains(r.TaskId)).ToDictionaryAsync(r => r.TaskId, ct);

        // ---- masalah terbuka
        var issues = new List<IssueDto>();
        foreach (var c in applicable)
        {
            if (!checkResults.TryGetValue(c.Id, out var r) || r.Status != CheckStatus.Failing) continue;
            var firstSeen = openAlerts.Where(a => a.AssignedCheckId == c.Id).Select(a => (DateTime?)a.AlertTime).Min();
            issues.Add(new IssueDto("check", r.AlertSeverity ?? c.AlertSeverity ?? AlertSeverity.Error, c.Name ?? c.CheckType, $"Check · {c.CheckType}",
                firstSeen, r.LastRun, Math.Max(r.FailCount, 1), r.Retcode?.ToString(), AgentDataParser.Compose(r.MoreInfo, r.Stdout, r.Stderr), c.Id));
        }
        foreach (var t in tasks)
        {
            if (!taskResults.TryGetValue(t.Id, out var r) || r.Status != CheckStatus.Failing) continue;
            var firstSeen = openAlerts.Where(a => a.AssignedTaskId == t.Id).Select(a => (DateTime?)a.AlertTime).Min();
            issues.Add(new IssueDto("task", t.AlertSeverity ?? AlertSeverity.Warning, $"Task '{t.Name}' failed", "Task terjadwal",
                firstSeen, r.LastRun, 1, r.Retcode?.ToString(), AgentDataParser.Compose(null, r.Stdout, r.Stderr), t.Id));
        }
        if (!online)
        {
            var since = openAlerts.Where(a => a.AlertType == AlertType.Availability).Select(a => (DateTime?)a.AlertTime).Min() ?? agent.LastSeen;
            issues.Add(new IssueDto("availability", AlertSeverity.Error, "Agent offline", "Ketersediaan agent", since, agent.LastSeen, 1, null,
                $"Terakhir lapor: {agent.LastSeen:u}\nAmbang offline: {agent.OfflineTime} menit", null));
        }
        foreach (var a in openAlerts.Where(a => a.AlertType == AlertType.Custom))
            issues.Add(new IssueDto("alert", a.Severity, a.Message ?? "Alert", "Alert kustom", a.AlertTime, a.AlertTime, 1, null, null, a.Id));
        var failedUpdates = updates.Where(u => u.Result == "failed").ToList();
        if (pendingCount >= 5 || failedUpdates.Count > 0)
            issues.Add(new IssueDto("patch", AlertSeverity.Warning,
                $"{pendingCount} update Windows tertunda" + (failedUpdates.Count > 0 ? $", {failedUpdates.Count} gagal dipasang" : ""), "Patch Windows", null, null, 1, null,
                string.Join("\n", updates.Take(20).Select(u => $"{u.Kb ?? "-"}  {u.Title}  [{(u.Result == "failed" ? "gagal" : "tertunda")}]")), null));
        if (outdated)
            issues.Add(new IssueDto("agent", AlertSeverity.Info, $"Agent {agent.Version} usang (terbaru {opt.Value.LatestAgentVer})", "Agent", null, null, 1, null, null, null));

        int Rank(string s) => s == AlertSeverity.Error ? 0 : s == AlertSeverity.Warning ? 1 : 2;
        issues = issues.OrderBy(i => Rank(i.Severity)).ThenByDescending(i => i.LastSeen).ToList();

        var facts = new AgentFactsDto(agent.AgentId, agent.Hostname, agent.Site.Client.Name, agent.Site.Name, agent.OperatingSystem ?? "", agent.Plat, agent.Version, outdated,
            agent.LastSeen, online, agent.PublicIp, agent.LoggedInUsername ?? agent.LastLoggedInUser, uptime, agent.NeedsReboot, agent.MaintenanceMode, agent.Description,
            agent.TotalRam is { } gb ? gb : null, agent.Goarch);

        var checkRows = applicable.Select(c => { checkResults.TryGetValue(c.Id, out var r);
            return new AgentCheckRow(c.Id, c.Name ?? c.CheckType, c.CheckType, r?.Status ?? CheckStatus.Pending, r?.LastRun, c.RunInterval > 0 ? c.RunInterval : agent.CheckInterval, r?.MoreInfo, c.PolicyId is not null); }).ToList();
        var taskRows = tasks.Select(t => { taskResults.TryGetValue(t.Id, out var r);
            return new AgentTaskRow(t.Id, t.Name, t.TaskType, t.Enabled, r?.Status ?? CheckStatus.Pending, r?.LastRun, r?.SyncStatus ?? TaskSyncStatus.Initial, t.PolicyId is not null); }).ToList();
        var patchRows = updates.Select(u => new AgentPatchRow(u.Kb, u.Title, u.Severity, u.Result == "failed" ? "failed" : u.Action == "ignore" ? "ignored" : "pending")).ToList();

        return Outcome.Ok(new AgentOverviewDto(facts, new HealthDto(health.Score, health.Level, health.Factors.Select(f => new HealthFactorDto(f.Code, f.Penalty, f.Detail)).ToList()),
            new AgentMetricsDto(cpu is null ? null : Math.Round(cpu.Value, 1), ram is null ? null : Math.Round(ram.Value, 1), worst), disks, issues, checkRows, taskRows, patchRows,
            AgentDataParser.ParseStoppedAutoServices(agent.Services)));
    }

    // ------------------------------------------------------------------------------------------ metrics
    public async Task<Outcome<MetricSeries>> GetMetricsAsync(User caller, string agentId, string metric, string? label, int days, CancellationToken ct = default)
    {
        var type = MetricRules.FromApiName(metric);
        if (type is null) return Outcome.Invalid<MetricSeries>("metric must be one of: cpu, memory, disk");
        days = Math.Clamp(days, 1, 90);

        var agent = await db.Agents.AsNoTracking().Include(a => a.Site).FirstOrDefaultAsync(a => a.AgentId == agentId, ct);
        if (agent is null) return Outcome.NotFound<MetricSeries>("Agent not found");
        if (!ScopeGuard.CanSeeAgent(caller, agent)) return Outcome.Forbidden<MetricSeries>();

        var from = DateTime.UtcNow.AddDays(-days);
        var q = db.CheckMetrics.AsNoTracking().Where(m => m.AgentId == agent.Id && m.CheckType == type && m.Time >= from);
        if (type == CheckType.DiskSpace && !string.IsNullOrWhiteSpace(label)) q = q.Where(m => m.Label == label);
        var pts = (await q.OrderBy(m => m.Time).Select(m => new { m.Time, m.Value }).ToListAsync(ct))
            .Select(m => new MetricPoint(DateTime.SpecifyKind(m.Time, DateTimeKind.Utc), m.Value)).ToList();
        return Outcome.Ok(new MetricSeries(MetricRules.ApiName(type), label, days, AgentDataParser.Downsample(pts, 1000)));
    }
}
