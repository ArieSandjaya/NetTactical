using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Abstractions;

public interface IFleetService
{
    /// <summary>Ringkasan aset sesuai scope user. clientId/siteId menyaring lebih lanjut; days = rentang tren (1-90).</summary>
    Task<Outcome<FleetSummaryDto>> GetSummaryAsync(User caller, int? clientId, int? siteId, int days, int riskLimit, CancellationToken ct = default);
    Task<Outcome<AgentOverviewDto>> GetOverviewAsync(User caller, string agentId, CancellationToken ct = default);
    /// <summary>metric = cpu | memory | disk; untuk disk, label = drive (mis. "C:").</summary>
    Task<Outcome<MetricSeries>> GetMetricsAsync(User caller, string agentId, string metric, string? label, int days, CancellationToken ct = default);
}
