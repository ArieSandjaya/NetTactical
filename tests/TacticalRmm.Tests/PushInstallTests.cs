using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class PushInstallTests
{
    private static readonly string Tok = new('a', 64);

    [Fact]
    public void ParsesMixedSeparatorsAndRemovesDuplicates()
    {
        var (h, e) = PushInstall.ParseTargets("PC-01\r\npc-01, 10.0.0.5;srv.corp.local  10.0.0.6");
        Assert.True(e is null, "no error");
        Assert.True(h.Count == 4 && h[0] == "PC-01" && h[1] == "10.0.0.5", "distinct, in order");
    }

    [Fact]
    public void RejectsDangerousOrMalformedTargets()
    {
        foreach (var bad in new[] { "127.0.0.1", "169.254.169.254", "0.0.0.0", "224.0.0.1", "localhost", "a.localhost", "2130706433", "1.2.3", "::1", "fe80::1", "host name!", "a;b|c", "-bad", "x..y", "http://pc", "pc/share", "0x7f.0.0.1" })
        {
            var (h, e) = PushInstall.ParseTargets(bad.Contains(' ') ? bad.Replace(' ', '_') : bad);
            Assert.True(e is not null && h.Count == 0, $"rejects '{bad}'");
        }
        Assert.True(PushInstall.ParseTargets("").Error is not null, "empty");
        Assert.True(PushInstall.ParseTargets(string.Join("\n", Enumerable.Range(1, 21).Select(i => $"pc{i}"))).Error is not null, "more than the maximum");
        Assert.True(PushInstall.ParseTargets(string.Join("\n", Enumerable.Range(1, 20).Select(i => $"pc{i}"))).Error is null, "exactly the maximum");
    }

    [Fact]
    public void AcceptsPrivateAddressesAndFqdn()
    {
        foreach (var ok in new[] { "10.1.2.3", "192.168.0.10", "172.16.5.5", "pc-01", "pc-01.plaza.local", "A1" })
            Assert.True(PushInstall.ValidateTarget(ok) is null, $"accepts '{ok}'");
    }

    [Fact]
    public void UsernameMustBeDomainAccount()
    {
        foreach (var ok in new[] { @"PLAZA\admin.arie", "plaza.local\\svc_deploy", "arie@plaza.local" })
            Assert.True(PushInstall.ValidateUsername(ok) is null, $"accepts {ok}");
        foreach (var bad in new[] { "", "administrator", @".\admin", @"PLAZA\", @"PLAZA\a b", "a@b", "x\"y\\z", "a@b.c;d" })
            Assert.True(PushInstall.ValidateUsername(bad) is not null, $"rejects '{bad}'");
        Assert.True(PushInstall.ValidatePassword("") is not null && PushInstall.ValidatePassword(new string('x', 300)) is not null && PushInstall.ValidatePassword("p@ss'\"") is null, "password rules");
    }

    [Fact]
    public void RemoteScriptContainsOnlyValidatedPartsAndHonoursForce()
    {
        var args = InstallerCommands.InstallArguments("https://rmm.example.com", 1, 2, "server", Tok, false, false, false);
        var url = InstallerCommands.DownloadUrl("2.12.0", "amd64")!;
        var file = InstallerCommands.FileName("2.12.0", "amd64");
        var s = PushInstall.RemoteScript(url, file, args, false);
        Assert.True(s.Contains("$force = $false") && s.Contains("return 'already_installed'") && s.Contains("--nomesh") && s.Contains(Tok), "script content");
        Assert.True(PushInstall.RemoteScript(url, file, args, true).Contains("$force = $true"), "force flag");
        Assert.Throws<ArgumentException>(() => PushInstall.RemoteScript("https://evil.example/x.exe", file, args, false));
        Assert.Throws<ArgumentException>(() => PushInstall.RemoteScript(url, "x'; calc; '.exe", args, false));
    }

    [Fact]
    public void CategorizesAuthFailuresSoThePushStops()
    {
        Assert.True(PushInstall.Categorize("Connecting to remote server PC failed: Access is denied.") == PushInstall.CatAuth, "access denied");
        Assert.True(PushInstall.Categorize("The user name or password is incorrect") == PushInstall.CatAuth, "bad password");
        Assert.True(PushInstall.Categorize("WinRM cannot process the request. The following error occurred") == PushInstall.CatUnreachable, "winrm");
        Assert.True(PushInstall.Categorize("Agent registration exited with code 1.") == PushInstall.CatError, "other");
    }

    [Fact]
    public void ScrubRemovesSecretsAndLimitsLength()
    {
        var t = PushInstall.Scrub("failed for hunter2 with hunter2", "hunter2", null, "");
        Assert.True(!t.Contains("hunter2") && t.Contains("***"), "secret removed");
        Assert.True(PushInstall.Scrub(new string('x', 5000)).Length <= 601, "truncated");
    }

    [Fact]
    public void RemoteRunRequestNeverPrintsThePassword()
    {
        var r = new RemoteRunRequest("pc", @"D\u", "hunter2", "script", false, 60);
        Assert.True(!r.ToString().Contains("hunter2"), "ToString is redacted");
    }
}
