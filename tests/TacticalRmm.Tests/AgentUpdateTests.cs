using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class AgentUpdateTests
{
    [Fact]
    public void AnOldWindowsAgentGetsTheUrlVersionAndInstallerName()
    {
        var p = AgentUpdatePlan.Decide("windows", "amd64", "2.8.0", "2.12.0");
        Assert.Null(p.Error); Assert.Null(p.Skip);
        Assert.Equal("agentupdate", p.Payload!["func"]);
        var pl = (IDictionary<string, object?>)p.Payload["payload"]!;
        Assert.Equal("https://github.com/ArieSandjaya/NetTactical/releases/download/agent-v2.12.0/nettacticalagent-v2.12.0-windows-amd64.exe", pl["url"]);
        Assert.Equal("2.12.0", pl["version"]);
        Assert.Equal("nettacticalagent-v2.12.0-windows-amd64.exe", pl["inno"]);
    }

    [Fact] public void ACurrentAgentIsSkipped() => Assert.NotNull(AgentUpdatePlan.Decide("windows", "amd64", "2.12.0", "2.12.0").Skip);
    [Fact] public void AnUnknownArchitectureDefaultsTo64Bit() => Assert.Contains("amd64", (string)((IDictionary<string, object?>)AgentUpdatePlan.Decide("windows", null, "1.0.0", "2.12.0").Payload!["payload"]!)["url"]!);
    [Fact] public void LinuxIsRefused() => Assert.NotNull(AgentUpdatePlan.Decide("linux", "amd64", "1.0.0", "2.12.0").Error);
    [Fact] public void ABadArchitectureOrVersionIsRefused()
    {
        Assert.NotNull(AgentUpdatePlan.Decide("windows", "../x", "1.0.0", "2.12.0").Error);
        Assert.NotNull(AgentUpdatePlan.Decide("windows", "amd64", "1.0.0", "latest").Error);
    }

    [Theory]
    [InlineData("started:Update started.", "ok")]
    [InlineData("skipped:Already on the latest version.", "ok")]
    [InlineData("offline:The agent did not answer.", "offline")]
    [InlineData("fail:NATS down", "failed")]
    public void BulkReadsTheUpdateReply(string reply, string status) => Assert.Equal(status, BulkRules.FromReply(BulkRules.AgentUpdateAction, reply).Status);

    [Fact] public void BulkUpdateNeedsTheUpdateAgentsPermission() => Assert.Equal(TacticalRmm.Domain.Perm.UpdateAgents, BulkRules.RequiredPermission("agent_update"));
}
