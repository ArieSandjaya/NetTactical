namespace TacticalRmm.Application.Dtos;

/// <summary>A report as a table. Total is the number of rows before the preview limit; Truncated means the data itself was cut at the report's maximum.</summary>
public record ReportTable(string Name, string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, bool Truncated, string Scope);

public record ReportInfo(string Name, string Title, string Description, bool UsesPeriod);

/// <summary>A report ready to download.</summary>
public record ReportFile(string FileName, byte[] Content);
