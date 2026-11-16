using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Abstractions;

public interface IReportService
{
    /// <summary>The reports this caller may run.</summary>
    IReadOnlyList<ReportInfo> Available(User caller);
    /// <summary>Builds a report inside the caller's client/site scope. from/to (yyyy-MM-dd) only matter for reports that use a period.</summary>
    /// <summary>q filters the software report by name, publisher or version. maxRows (for the on-screen preview) keeps only the first rows; Total still counts all.</summary>
    Task<Outcome<ReportTable>> BuildAsync(User caller, string name, int? clientId, int? siteId, string? from, string? to, string? q = null, int? maxRows = null, CancellationToken ct = default);
    Task<Outcome<ReportFile>> CsvAsync(User caller, string name, int? clientId, int? siteId, string? from, string? to, string? q = null, CancellationToken ct = default);
}

public interface ISoftwareService
{
    /// <summary>The last list the asset sent (empty with UpdatedAt = null when it never did).</summary>
    Task<Outcome<SoftwareDto>> GetAsync(string agentId, CancellationToken ct = default);
    /// <summary>Asks the agent for its list now (NATS "softwarelist"), stores it and returns it. Conflict when the agent does not answer.</summary>
    Task<Outcome<SoftwareDto>> RefreshAsync(string agentId, string username, CancellationToken ct = default);
}
