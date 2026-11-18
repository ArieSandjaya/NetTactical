using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using Xunit;

namespace TacticalRmm.Tests;

public class BulkRulesTests
{
    private static string Id(int n) => "agent-id-number-" + n.ToString("D6");
    private static BulkRequest Cmd(params string[] ids) => new("command", ids.ToList(), "ipconfig", "cmd", 30);

    [Fact]
    public void AValidCommandRequestPasses() => Assert.Null(BulkRules.Validate(Cmd(Id(1), Id(2))));

    [Fact]
    public void TheActionDecidesThePermission()
    {
        Assert.Equal(Perm.RunCommands, BulkRules.RequiredPermission("command"));
        Assert.Equal(Perm.RunScripts, BulkRules.RequiredPermission("script"));
        Assert.Equal(Perm.RebootAgents, BulkRules.RequiredPermission("reboot"));
        Assert.Null(BulkRules.RequiredPermission("format-disk"));
        Assert.NotNull(BulkRules.Validate(new BulkRequest("format-disk", [Id(1)])));
    }

    [Fact]
    public void TargetsMustBeAFewDistinctValidIds()
    {
        Assert.NotNull(BulkRules.Validate(Cmd()));
        Assert.NotNull(BulkRules.Validate(new BulkRequest("command", null, "x", "cmd")));
        Assert.NotNull(BulkRules.Validate(Cmd(Id(1), Id(1))));
        Assert.NotNull(BulkRules.Validate(Cmd("short")));
        Assert.NotNull(BulkRules.Validate(Cmd(Enumerable.Range(0, BulkRules.MaxTargets + 1).Select(Id).ToArray())));
        Assert.Null(BulkRules.Validate(Cmd(Enumerable.Range(0, BulkRules.MaxTargets).Select(Id).ToArray())));
    }

    [Theory]
    [InlineData("", "cmd", 30)]
    [InlineData("  ", "cmd", 30)]
    [InlineData("dir", "zsh", 30)]
    [InlineData("dir", "cmd", 0)]
    [InlineData("dir", "cmd", 301)]
    public void ACommandNeedsTextAKnownShellAndATimeout(string cmd, string shell, int timeout) =>
        Assert.NotNull(BulkRules.Validate(new BulkRequest("command", [Id(1)], cmd, shell, timeout)));

    [Fact]
    public void AScriptNeedsAnId()
    {
        Assert.NotNull(BulkRules.Validate(new BulkRequest("script", [Id(1)], Script: 0)));
        Assert.Null(BulkRules.Validate(new BulkRequest("script", [Id(1)], Script: 5)));
    }

    [Fact]
    public void ARebootNeedsNoCommandOrTimeout() => Assert.Null(BulkRules.Validate(new BulkRequest("reboot", [Id(1)], Timeout: 0)));

    [Fact]
    public void RepliesBecomeStatuses()
    {
        Assert.Equal(("offline", "The agent did not answer (it may be offline)."), BulkRules.FromReply("command", "timeout"));
        Assert.Equal("failed", BulkRules.FromReply("command", "natsdown").Status);
        Assert.Equal(("ok", "Windows IP"), BulkRules.FromReply("command", "  Windows IP \n"));
        Assert.Equal("ok", BulkRules.FromReply("reboot", "ok").Status);
        Assert.Equal("failed", BulkRules.FromReply("reboot", "nope").Status);
        Assert.Equal("failed", BulkRules.FromReply("reboot", 5).Status);
    }

    [Fact]
    public void AScriptResultFailsOnAReturnCodeThatIsNotZero()
    {
        var good = new Dictionary<object, object?> { ["stdout"] = "done", ["stderr"] = "", ["retcode"] = 0L };
        var bad = new Dictionary<object, object?> { ["stdout"] = "partial", ["stderr"] = "boom", ["retcode"] = 2L };
        Assert.Equal(("ok", "done"), BulkRules.FromReply("script", good));
        Assert.Equal(("failed", "partial\nboom"), BulkRules.FromReply("script", bad));
    }

    [Fact]
    public void LongOutputIsCut()
    {
        var (_, msg) = BulkRules.FromReply("command", new string('x', 5000));
        Assert.Equal(BulkRules.MaxOutput + 1, msg!.Length);   // text plus the ellipsis
    }
}
