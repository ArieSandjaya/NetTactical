namespace TacticalRmm.Application.Dtos;

public record ServiceDto(string Name, string? DisplayName, string? Status, string? StartType, string? Username);
public record ProcessDto(int Pid, string Name, long? MemBytes, string? Username, double? Cpu);

/// <summary>Action is start, stop or restart.</summary>
public record ServiceActionRequest(string? Name, string? Action);
public record KillRequest(int Pid, string? Name = null);
public record ToolResult(string Message);

/// <summary>Result of asking one agent to update itself. Status: started, skipped.</summary>
public record AgentUpdateDto(string Status, string Message);
