using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class FileBrowserTests
{
    [Theory]
    [InlineData(@"C:\", true)] [InlineData(@"C:\Users\Bob", true)] [InlineData("c:/temp", true)] [InlineData(@"\\srv\share\x", true)] [InlineData("C:", true)]
    [InlineData("", false)] [InlineData("relative\\path", false)] [InlineData(@"C:\*", false)] [InlineData("C:\\a\nb", false)] [InlineData(@"\\", false)] [InlineData(@"C:\a|b", false)]
    public void PathsMustBeAbsoluteAndPlain(string path, bool ok) => Assert.Equal(ok, FileBrowser.ValidPath(path));

    [Fact] public void TheEmptyPathIsTheDriveListOnlyWhenAllowed() { Assert.True(FileBrowser.ValidPath("", true)); Assert.True(FileBrowser.ValidPath(null, true)); Assert.False(FileBrowser.ValidPath(null)); }

    [Theory]
    [InlineData("report.pdf", true)] [InlineData("my file (1).txt", true)] [InlineData("a'b", true)]
    [InlineData("", false)] [InlineData("..", false)] [InlineData(".", false)] [InlineData("a\\b", false)] [InlineData("a/b", false)] [InlineData("a:b", false)] [InlineData("x.", false)] [InlineData("x ", false)]
    public void NamesHaveNoSeparators(string name, bool ok) => Assert.Equal(ok, FileBrowser.ValidName(name));

    [Theory]
    [InlineData(@"C:\", true)] [InlineData("C:", true)] [InlineData(@"C:\Windows", false)] [InlineData(@"\\srv\share", true)] [InlineData(@"\\srv\share\dir", false)]
    public void RootsAreNotDeletable(string path, bool root) => Assert.Equal(root, FileBrowser.IsRoot(path));

    [Theory]
    [InlineData(@"C:\Users\Bob", @"C:\Users")] [InlineData(@"C:\Users", @"C:\")] [InlineData(@"C:\", "")] [InlineData(@"C:\Users\", @"C:\")]
    public void ParentGoesUpOneLevel(string path, string parent) => Assert.Equal(parent, FileBrowser.Parent(path));

    [Fact] public void SingleQuotesAreDoubledSoANameCannotBreakOutOfTheScript()
    {
        Assert.Equal("'it''s'", FileBrowser.Q("it's"));
        var s = FileBrowser.DeleteScript(@"C:\x'; Remove-Item C:\ -Recurse; '", false);
        Assert.Contains(@"'C:\x''; Remove-Item C:\ -Recurse; '''", s);
    }

    [Fact] public void ScriptsCarryTheirOptions()
    {
        Assert.Contains("-not $true", FileBrowser.UploadScript(@"C:\d", "f.txt", "QQ==", true));
        Assert.Contains("-not $false", FileBrowser.UploadScript(@"C:\d", "f.txt", "QQ==", false));
        Assert.Contains("'QQ=='", FileBrowser.UploadScript(@"C:\d", "f.txt", "QQ==", false));
        Assert.Contains("Get-PSDrive", FileBrowser.ListScript(""));
    }

    [Fact] public void OutputReadsTheMapTheAgentReturns()
    {
        var ok = new Dictionary<object, object?> { ["stdout"] = "noise\r\n@@OK@@[]", ["stderr"] = "", ["retcode"] = 0 };
        Assert.Equal(("[]", null), FileBrowser.Payload(FileBrowser.Output(ok).Text));
        var err = new Dictionary<object, object?> { ["stdout"] = "@@ERR@@ Access to the path is denied.", ["retcode"] = 0 };
        Assert.Equal((null, "Access to the path is denied."), FileBrowser.Payload(FileBrowser.Output(err).Text));
        var broken = new Dictionary<object, object?> { ["stdout"] = "", ["stderr"] = "ParserError", ["retcode"] = 1 };
        Assert.Equal("ParserError", FileBrowser.Output(broken).Error);
        Assert.Contains("offline", FileBrowser.Output("timeout").Error);
        Assert.Contains("NATS", FileBrowser.Output("natsdown").Error);
        Assert.NotNull(FileBrowser.Output(42).Error);
    }

    [Fact] public void ListParsingSortsFoldersFirstAndFlagsTruncation()
    {
        var json = """[{"name":"b.txt","dir":false,"size":5,"modified":"2024-01-02T00:00:00.0000000Z"},{"name":"Zed","dir":true,"size":null,"modified":null},{"name":"a","dir":true}]""";
        var l = FileBrowser.ParseList(@"C:\Users", json)!;
        Assert.Equal(["a", "Zed", "b.txt"], l.Entries.Select(e => e.Name));
        Assert.Equal(@"C:\", l.Parent); Assert.False(l.Truncated); Assert.Equal(5, l.Entries[2].Size);
        Assert.Null(FileBrowser.ParseList("", "{}"));
        Assert.Null(FileBrowser.ParseList("", "not json"));
        var drives = FileBrowser.ParseList("", """[{"name":"C:\\","dir":true,"free":100,"used":300}]""")!;
        Assert.Null(drives.Parent); Assert.Equal(100, drives.Entries[0].Free);
    }

    [Fact] public void ABase64PayloadIsDecodedOrRejected() { Assert.Equal([65], FileBrowser.ParseBase64("QQ==")); Assert.Null(FileBrowser.ParseBase64("%%%")); Assert.Equal("a.txt", FileBrowser.DownloadName(@"C:\x\a.txt")); }
}
