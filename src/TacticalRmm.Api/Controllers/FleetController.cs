using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TacticalRmm.Api.Auth;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Domain;

namespace TacticalRmm.Api.Controllers;

/// <summary>Endpoints for the asset dashboard (summary, device detail, historical metrics).</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy)]
public class FleetController(IFleetService svc) : ControllerBase
{
    [HttpGet("fleet/summary"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> Summary([FromQuery(Name = "client_id")] int? clientId, [FromQuery(Name = "site_id")] int? siteId,
        [FromQuery] int days = 30, [FromQuery(Name = "risk_limit")] int riskLimit = 10, CancellationToken ct = default) =>
        this.ToResult(await svc.GetSummaryAsync(HttpContext.GetUser(), clientId, siteId, days, riskLimit, ct));

    [HttpGet("agents/{agentId}/overview"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> Overview(string agentId, CancellationToken ct) =>
        this.ToResult(await svc.GetOverviewAsync(HttpContext.GetUser(), agentId, ct));

    [HttpGet("agents/{agentId}/metrics"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> Metrics(string agentId, [FromQuery] string metric = "cpu", [FromQuery] string? label = null,
        [FromQuery] int days = 30, CancellationToken ct = default) =>
        this.ToResult(await svc.GetMetricsAsync(HttpContext.GetUser(), agentId, metric, label, days, ct));
}
