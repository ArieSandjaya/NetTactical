namespace TacticalRmm.Application.Dtos;

public record NoteRequest(string? Note);
public record NoteDto(int Id, string Note, string? Username, DateTime EntryTime);

/// <summary>One command, script or task run on an asset. The result fields are filled when the run reported one.</summary>
public record HistoryEntryDto(int Id, DateTime Time, string Type, string? Command, string? Username, string? Stdout, string? Stderr, int? Retcode, double? ExecutionTime);
