using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>
/// Three reports built from data the dashboard already has: asset inventory, outstanding patches and alert history.
/// Everything stays inside the caller's client/site scope, and each report needs the permission of the data it shows.
/// </summary>
public sealed class ReportService(AppDbContext db, IFleetService fleet) : IReportService
{
    private const int MaxAlertRows = 5000, MaxSoftwareRows = 200_000;
    private static readonly ReportInfo[] All =
    [
        new("assets", "Asset inventory", "Every asset with its client, site, operating system, agent version, last check-in, health score, pending updates and open alerts.", false),
        new("patches", "Outstanding patches", "Assets that have pending or failed Windows updates, or that need a reboot.", false),
        new("alerts", "Alert history", "Alerts raised in the chosen period, open and resolved (up to 5000).", true),
        new("software", "Installed software", "Every application each asset reported, one row per asset and application. Type a name, publisher or version to find who has it. Assets that never sent a list are not included.", false, true),
    ];

    private static bool Allowed(User u, string name) => name switch
    {
        "assets" or "patches" or "software" => PermissionEvaluator.Has(u, Perm.ListAgents),
        "alerts" => PermissionEvaluator.Has(u, Perm.ListAlerts),
        _ => false,
    };

    public IReadOnlyList<ReportInfo> Available(User caller) => All.Where(r => Allowed(caller, r.Name)).ToList();

    private static string Time(DateTime? t) => t is null ? "" : DateTime.SpecifyKind(t.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private static string YesNo(bool b) => b ? "Yes" : "No";

    public async Task<Outcome<ReportTable>> BuildAsync(User caller, string name, int? clientId, int? siteId, string? from, string? to, string? q = null, int? maxRows = null, CancellationToken ct = default)
    {
        var r = await BuildFullAsync(caller, name, clientId, siteId, from, to, q, ct);
        if (!r.IsOk || maxRows is null || r.Value!.Rows.Count <= maxRows) return r;
        return Outcome.Ok(r.Value with { Rows = r.Value.Rows.Take(maxRows.Value).ToList() });
    }

    private async Task<Outcome<ReportTable>> BuildFullAsync(User caller, string name, int? clientId, int? siteId, string? from, string? to, string? q, CancellationToken ct)
    {
        var info = All.FirstOrDefault(r => r.Name == name);
        if (info is null) return Outcome.NotFound<ReportTable>("Unknown report");
        if (!Allowed(caller, name)) return Outcome.Forbidden<ReportTable>();
        switch (name)
        {
            case "assets":
            {
                var r = await fleet.GetAssetsAsync(caller, clientId, siteId, ct);
                if (!r.IsOk) return Outcome.Fail<ReportTable>(r.Status, r.Error);
                string[] cols = ["Client", "Site", "Hostname", "Operating system", "Agent version", "Status", "Last seen (UTC)", "Health score", "Pending updates", "Open alerts", "Needs reboot", "Uptime (days)"];
                var rows = r.Value!.Assets.OrderBy(a => a.Client).ThenBy(a => a.Site).ThenBy(a => a.Hostname).Select(a => (IReadOnlyList<string>)
                    [a.Client, a.Site, a.Hostname, a.Os, a.Version ?? "", a.Online ? "Online" : "Offline", Time(a.LastSeen), a.Score.ToString(CultureInfo.InvariantCulture),
                     a.PendingUpdates.ToString(CultureInfo.InvariantCulture), a.OpenAlerts.ToString(CultureInfo.InvariantCulture), YesNo(a.NeedsReboot), a.UptimeDays?.ToString(CultureInfo.InvariantCulture) ?? ""]).ToList();
                return Outcome.Ok(new ReportTable(name, info.Title, cols, rows, false, r.Value.Scope.Name, rows.Count));
            }
            case "patches":
            {
                var r = await fleet.GetPatchesAsync(caller, clientId, siteId, ct);
                if (!r.IsOk) return Outcome.Fail<ReportTable>(r.Status, r.Error);
                string[] cols = ["Client", "Hostname", "Status", "Pending updates", "Failed updates", "Needs reboot"];
                var rows = r.Value!.Devices.OrderBy(d => d.Client).ThenBy(d => d.Hostname).Select(d => (IReadOnlyList<string>)
                    [d.Client, d.Hostname, d.Online ? "Online" : "Offline", d.Pending.ToString(CultureInfo.InvariantCulture), d.Failed.ToString(CultureInfo.InvariantCulture), YesNo(d.NeedsReboot)]).ToList();
                return Outcome.Ok(new ReportTable(name, info.Title, cols, rows, false, r.Value.Scope.Name, rows.Count));
            }
            case "software":
                return await SoftwareAsync(caller, info, clientId, siteId, q, ct);
            default:
                return await AlertsAsync(caller, info, clientId, siteId, from, to, ct);
        }
    }

    /// <summary>One row per asset and application. The assets come from the same scoped list as the asset inventory.</summary>
    private async Task<Outcome<ReportTable>> SoftwareAsync(User caller, ReportInfo info, int? clientId, int? siteId, string? q, CancellationToken ct)
    {
        var assets = await fleet.GetAssetsAsync(caller, clientId, siteId, ct);
        if (!assets.IsOk) return Outcome.Fail<ReportTable>(assets.Status, assets.Error);
        var byAgentId = assets.Value!.Assets.ToDictionary(a => a.AgentId);
        var ids = byAgentId.Keys.ToList();
        var stored = await db.InstalledSoftware.AsNoTracking()
            .Join(db.Agents.Where(a => ids.Contains(a.AgentId)), s => s.AgentId, a => a.Id, (s, a) => new { a.AgentId, s.Software, s.UpdatedAt }).ToListAsync(ct);
        string[] cols = ["Client", "Site", "Hostname", "Software", "Version", "Publisher", "Install date", "List received (UTC)"];
        var rows = new List<IReadOnlyList<string>>();
        var truncated = false;
        foreach (var s in stored.OrderBy(x => byAgentId[x.AgentId].Client).ThenBy(x => byAgentId[x.AgentId].Site).ThenBy(x => byAgentId[x.AgentId].Hostname))
        {
            var a = byAgentId[s.AgentId];
            foreach (var i in SoftwareList.Filter(SoftwareList.ParseJson(s.Software), q))
            {
                if (rows.Count >= MaxSoftwareRows) { truncated = true; break; }
                rows.Add([a.Client, a.Site, a.Hostname, i.Name, i.Version ?? "", i.Publisher ?? "", i.InstallDate ?? "", Time(s.UpdatedAt)]);
            }
            if (truncated) break;
        }
        var scope = string.IsNullOrWhiteSpace(q) ? assets.Value.Scope.Name : $"{assets.Value.Scope.Name}, matching \"{q.Trim()}\"";
        return Outcome.Ok(new ReportTable(info.Name, info.Title, cols, rows, truncated, scope, rows.Count));
    }

    private async Task<Outcome<ReportTable>> AlertsAsync(User caller, ReportInfo info, int? clientId, int? siteId, string? from, string? to, CancellationToken ct)
    {
        var (f, t, err) = ReportRules.Range(from, to, DateTime.UtcNow);
        if (err is not null) return Outcome.Invalid<ReportTable>(err);
        var q = db.Alerts.AsNoTracking().Where(a => !a.Hidden && a.AlertTime >= f && a.AlertTime < t);
        if (siteId is { } sid) q = q.Where(a => a.Agent != null && a.Agent.SiteId == sid);
        else if (clientId is { } cid) q = q.Where(a => a.Agent != null && a.Agent.Site.ClientId == cid);
        var rows = await q.Include(a => a.Agent!).ThenInclude(g => g.Site).ThenInclude(s => s.Client)
            .OrderByDescending(a => a.AlertTime).Take(MaxAlertRows + 1).ToListAsync(ct);
        var truncated = rows.Count > MaxAlertRows;
        var seen = rows.Take(MaxAlertRows).Where(a => a.Agent is null || PermissionEvaluator.CanSeeSite(caller, a.Agent.Site)).ToList();
        string[] cols = ["Time (UTC)", "Severity", "Type", "Client", "Site", "Hostname", "Message", "Status", "Resolved (UTC)"];
        var table = seen.Select(a => (IReadOnlyList<string>)
            [Time(a.AlertTime), a.Severity, a.AlertType, a.Agent?.Site.Client.Name ?? "", a.Agent?.Site.Name ?? "", a.Agent?.Hostname ?? "", a.Message ?? "", a.Resolved ? "Resolved" : "Open", Time(a.ResolvedOn)]).ToList();
        var scope = $"{f:yyyy-MM-dd} to {t.AddDays(-1):yyyy-MM-dd}";
        return Outcome.Ok(new ReportTable(info.Name, info.Title, cols, table, truncated, scope, table.Count));
    }

    public async Task<Outcome<ReportFile>> CsvAsync(User caller, string name, int? clientId, int? siteId, string? from, string? to, string? q = null, CancellationToken ct = default)
    {
        var r = await BuildFullAsync(caller, name, clientId, siteId, from, to, q, ct);
        if (!r.IsOk) return Outcome.Fail<ReportFile>(r.Status, r.Error);
        var text = ReportRules.Csv(r.Value!.Columns, r.Value.Rows);
        // A byte order mark makes Excel read the file as UTF-8 (host and client names may have any characters).
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        return Outcome.Ok(new ReportFile($"nettactical-{name}-{DateTime.UtcNow:yyyyMMdd}.csv", bytes));
    }
}
