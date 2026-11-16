using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Abstractions;

public interface IReportService
{
    /// <summary>The reports this caller may run.</summary>
    IReadOnlyList<ReportInfo> Available(User caller);
    /// <summary>Builds a report inside the caller's client/site scope. from/to (yyyy-MM-dd) only matter for reports that use a period.</summary>
    Task<Outcome<ReportTable>> BuildAsync(User caller, string name, int? clientId, int? siteId, string? from, string? to, CancellationToken ct = default);
    Task<Outcome<ReportFile>> CsvAsync(User caller, string name, int? clientId, int? siteId, string? from, string? to, CancellationToken ct = default);
}
