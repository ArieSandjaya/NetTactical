namespace TacticalRmm.Application.Dtos;

/// <summary>One action for several assets. Only the fields of the chosen action are used.</summary>
public record BulkRequest(string? Action, List<string>? AgentIds, string? Cmd = null, string? Shell = null, int Timeout = 30,
    int Script = 0, List<string>? Args = null, bool RunAsUser = false,
    string? Url = null, string? Sha256 = null, string? InstallArgs = null, bool Reinstall = false);

public record BulkTargetDto(string AgentId, string Hostname, string Status, string? Message);

/// <summary>State is "running" or "done". Target status: queued, running, ok, failed, offline.</summary>
public record BulkJobDto(string Id, string Action, string State, DateTime CreatedAt, DateTime? FinishedAt, string Username, IReadOnlyList<BulkTargetDto> Targets);
