using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class RemoteToolsTests
{
    private static Dictionary<object, object?> Map(params (string k, object? v)[] kv) => kv.ToDictionary(x => (object)x.k, x => x.v);

    [Theory]
    [InlineData("Spooler", true)]
    [InlineData("Windows Update", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("a\nb", false)]
    public void ServiceNamesAreChecked(string? n, bool ok) => Assert.Equal(ok, RemoteTools.ValidServiceName(n));

    [Fact]
    public void AVeryLongServiceNameIsRefused() => Assert.False(RemoteTools.ValidServiceName(new string('a', 257)));

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(-1, false)]
    [InlineData(5, true)]
    [InlineData(1234, true)]
    public void SystemProcessIdsAreRefused(int pid, bool ok) => Assert.Equal(ok, RemoteTools.ValidPid(pid));

    [Fact]
    public void ServicesAreReadAndSortedByDisplayName()
    {
        object reply = new List<object?>
        {
            Map(("name", "wuauserv"), ("display_name", "Windows Update"), ("status", "running"), ("start_type", "manual")),
            Map(("name", "Spooler"), ("display_name", "Print Spooler"), ("status", "stopped"), ("start_type", "auto"), ("username", "LocalSystem")),
            Map(("display_name", "no name")),           // skipped
            "junk",                                      // skipped
        };
        var list = RemoteTools.ServicesFromReply(reply)!;
        Assert.Equal(new[] { "Spooler", "wuauserv" }, list.Select(s => s.Name).ToArray());
        Assert.Equal(("stopped", "auto", "LocalSystem"), (list[0].Status, list[0].StartType, list[0].Username));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData(null)]
    public void ARepliedWordOrNothingIsNotAList(object? reply)
    {
        Assert.Null(RemoteTools.ServicesFromReply(reply));
        Assert.Null(RemoteTools.ProcessesFromReply(reply));
    }

    [Fact]
    public void ASingleMapIsNotAList() => Assert.Null(RemoteTools.ProcessesFromReply(Map(("name", "x"), ("pid", 5))));

    [Fact]
    public void ProcessesAreReadWithWhateverNumberTypeTheAgentSent()
    {
        object reply = new List<object?>
        {
            Map(("name", "chrome.exe"), ("pid", 4321L), ("membytes", 104857600UL), ("username", "CORP\\bob"), ("cpu_percent", 12.5)),
            Map(("name", "Idle"), ("pid", 0), ("membytes", null)),
            Map(("name", "bad pid"), ("pid", "x")),       // skipped
            Map(("name", "app"), ("pid", 7)),
        };
        var list = RemoteTools.ProcessesFromReply(reply)!;
        Assert.Equal(new[] { "app", "chrome.exe", "Idle" }, list.Select(p => p.Name).ToArray());
        var chrome = list.Single(p => p.Name == "chrome.exe");
        Assert.Equal((4321, 104857600L, 12.5, "CORP\\bob"), (chrome.Pid, chrome.MemBytes, chrome.Cpu, chrome.Username));
    }

    [Fact]
    public void AServiceActionSucceedsOnlyWithSuccessTrue()
    {
        Assert.True(RemoteTools.ServiceActionResult(Map(("success", true), ("errormsg", ""))).Ok);
        Assert.Equal((false, "Access is denied."), RemoteTools.ServiceActionResult(Map(("success", false), ("errormsg", "Access is denied."))));
        Assert.False(RemoteTools.ServiceActionResult(Map(("errormsg", "x"))).Ok);
        Assert.False(RemoteTools.ServiceActionResult("timeout").Ok);
        Assert.Equal("The NATS server is not reachable.", RemoteTools.ServiceActionResult("natsdown").Message);
        Assert.False(RemoteTools.ServiceActionResult(null).Ok);
    }

    [Fact]
    public void KillOnlyCountsWhenTheAgentSaysOk()
    {
        Assert.True(RemoteTools.KillResult("ok").Ok);
        Assert.Equal((false, "Access is denied."), RemoteTools.KillResult("Access is denied."));
        Assert.False(RemoteTools.KillResult("timeout").Ok);
        Assert.False(RemoteTools.KillResult(5).Ok);
    }
}
