namespace TacticalRmm.Application.Dtos;

public class AssetGroupUpsert
{
    public string? Name { get; set; }
    /// <summary>null = leave as is, empty = clear.</summary>
    public string? Description { get; set; }
    public int? Policy { get; set; }
    public bool? ClearPolicy { get; set; }
}

public class GroupMembersRequest { public List<string>? AgentIds { get; set; } }

public record AssetGroupDto(int Id, string Name, string? Description, int? PolicyId, string? PolicyName, int Members);
public record GroupMemberDto(string AgentId, string Hostname, string Client, string Site, bool Online);
public record AssetGroupDetailDto(int Id, string Name, string? Description, int? PolicyId, string? PolicyName, int Members, IReadOnlyList<GroupMemberDto> Items);
