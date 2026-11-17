using TacticalRmm.Application.Logic;

namespace TacticalRmm.Application.Dtos;

/// <summary>The installed applications of one asset. UpdatedAt is null when no list has been received yet.</summary>
public record SoftwareDto(IReadOnlyList<SoftwareItem> Items, DateTime? UpdatedAt);
