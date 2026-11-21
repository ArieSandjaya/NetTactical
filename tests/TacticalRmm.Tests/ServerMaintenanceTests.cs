using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class ServerMaintenanceTests
{
    [Theory]
    [InlineData("prune_audit", 30, null)]
    [InlineData("prune_audit", 29, "Keep at least 30 days for this action.")]
    [InlineData("prune_alerts", 7, null)]
    [InlineData("prune_tokens", 1, null)]
    [InlineData("stale_agents", 29, "Keep at least 30 days for this action.")]
    [InlineData("stale_agents", 4000, "Days must be 3650 or less.")]
    [InlineData("drop_database", 90, "Unknown maintenance action.")]
    [InlineData(null, 90, "Unknown maintenance action.")]
    public void Validates(string? action, int days, string? expected) => Assert.Equal(expected, ServerMaintenance.Validate(action, days));

    [Fact]
    public void CutoffIsDaysBeforeNow() => Assert.Equal(new DateTime(2026, 1, 1), ServerMaintenance.Cutoff(10, new DateTime(2026, 1, 11)));
}
