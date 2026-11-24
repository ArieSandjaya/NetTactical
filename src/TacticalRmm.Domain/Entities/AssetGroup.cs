namespace TacticalRmm.Domain.Entities;

/// <summary>A hand-made set of assets, from any client or site. A policy set on the group applies to every member, so policies no longer
/// have to be assigned asset by asset. An asset is in at most one group. The chain is: asset, then its group, then site, then client.</summary>
public class AssetGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
