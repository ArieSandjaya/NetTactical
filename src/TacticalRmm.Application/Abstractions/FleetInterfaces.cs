using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Abstractions;

public interface IFleetService
{
    /// <summary>Asset summary within the user's scope. clientId/siteId filter further; days = trend range (1-90).</summary>
    Task<Outcome<FleetSummaryDto>> GetSummaryAsync(User caller, int? clientId, int? siteId, int days, int riskLimit, CancellationToken ct = default);
    /// <summary>All assets in scope with health score, sorted by lowest score.</summary>
    Task<Outcome<FleetAssetsDto>> GetAssetsAsync(User caller, int? clientId, int? siteId, CancellationToken ct = default);
    /// <summary>Outstanding (pending/failed) Windows updates across assets in scope.</summary>
    Task<Outcome<FleetPatchesDto>> GetPatchesAsync(User caller, int? clientId, int? siteId, CancellationToken ct = default);
    Task<Outcome<AgentOverviewDto>> GetOverviewAsync(User caller, string agentId, CancellationToken ct = default);
    /// <summary>metric = cpu | memory | disk; for disk, label = drive (mis. "C:").</summary>
    Task<Outcome<MetricSeries>> GetMetricsAsync(User caller, string agentId, string metric, string? label, int days, CancellationToken ct = default);
}
