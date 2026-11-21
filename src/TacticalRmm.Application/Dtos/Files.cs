namespace TacticalRmm.Application.Dtos;

/// <summary>One file or folder. For a drive row (the top level) Name is the drive root such as C:\ and Free/Used are filled.</summary>
public record FileEntryDto(string Name, bool IsDir, long? Size, string? Modified, long? Free = null, long? Used = null);
/// <summary>Path is empty at the top level (the drives). Parent is null there too.</summary>
public record FileListDto(string Path, string? Parent, IReadOnlyList<FileEntryDto> Entries, bool Truncated);
public record FileDownloadDto(string FileName, byte[] Content);

public record FileUploadRequest(string? Path, string? Name, string? ContentBase64, bool Overwrite = false);
public record FolderRequest(string? Path, string? Name);
public record FileDeleteRequest(string? Path, bool Recursive = false);
public record FileRenameRequest(string? Path, string? NewName);

/// <summary>Windows event log. Level is the display name (Error, Warning, Information, Critical, Verbose).</summary>
public record EventLogEntryDto(string? Time, int Id, string? Level, string? Provider, string? Message, long? RecordId);
public record EventLogDto(string Log, IReadOnlyList<EventLogEntryDto> Entries, bool Truncated);
