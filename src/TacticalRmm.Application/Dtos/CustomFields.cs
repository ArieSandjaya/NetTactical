namespace TacticalRmm.Application.Dtos;

public class CustomFieldUpsert
{
    public string? Name { get; set; }
    public string? Model { get; set; }      // only on create; a field cannot change what it belongs to
    public string? Type { get; set; }       // only on create
    public string? Options { get; set; }
    public string? DefaultValue { get; set; }
}

public record CustomFieldDto(int Id, string Name, string Model, string Type, string? Options, string? DefaultValue, int ValueCount);

/// <summary>One field with the value of a given client, site or agent. Value is "" when none is set; Effective applies the default.</summary>
public record CustomFieldValueDto(int FieldId, string Name, string Type, string? Options, string? DefaultValue, string Value, string Effective);

public class CustomFieldValuesSet
{
    /// <summary>Field id to new value. A blank value removes the value (the default applies again). Fields not listed are left alone.</summary>
    public Dictionary<int, string?> Values { get; set; } = new();
}
