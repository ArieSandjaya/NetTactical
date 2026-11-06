using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using Xunit;

namespace TacticalRmm.Tests;

public class CheckEvaluatorTests
{
    private static CheckResultPayload P(Action<CheckResultPayload> f) { var p = new CheckResultPayload(); f(p); return p; }

    [Theory]
    [InlineData(95, CheckStatus.Failing, AlertSeverity.Error)]
    [InlineData(85, CheckStatus.Failing, AlertSeverity.Warning)]
    [InlineData(40, CheckStatus.Passing, null)]
    public void Disk_thresholds(double used, string status, string? sev)
    {
        var chk = new Check { CheckType = CheckType.DiskSpace, ErrorThreshold = 10, WarningThreshold = 20 };
        var r = new CheckResult();
        CheckEvaluator.Apply(chk, r, P(p => { p.Exists = true; p.PercentUsed = used; }));
        Assert.Equal(status, r.Status);
        Assert.Equal(sev, r.AlertSeverity);
    }

    [Fact]
    public void Cpu_history_is_capped_at_15()
    {
        var chk = new Check { CheckType = CheckType.CpuLoad, ErrorThreshold = 90, WarningThreshold = 70 };
        var r = new CheckResult();
        for (var i = 0; i < 20; i++) CheckEvaluator.Apply(chk, r, P(p => p.Percent = 80));
        Assert.Equal(15, r.History.Count);
        Assert.Equal(AlertSeverity.Warning, r.AlertSeverity);
    }

    [Theory]
    [InlineData(5, AlertSeverity.Info)]
    [InlineData(3, AlertSeverity.Warning)]
    [InlineData(1, AlertSeverity.Error)]
    public void Script_return_codes(long rc, string sev)
    {
        var chk = new Check { CheckType = CheckType.Script, InfoReturnCodes = [5], WarningReturnCodes = [3] };
        var r = new CheckResult();
        CheckEvaluator.Apply(chk, r, P(p => { p.Retcode = rc; p.Stdout = ""; p.Stderr = ""; p.Runtime = 1; }));
        Assert.Equal(sev, r.AlertSeverity);
    }

    [Fact]
    public void Alert_only_after_fails_b4_alert()
    {
        var chk = new Check { FailsB4Alert = 3 };
        Assert.False(CheckEvaluator.ShouldAlert(chk, new CheckResult { Status = CheckStatus.Failing, FailCount = 2 }));
        Assert.True(CheckEvaluator.ShouldAlert(chk, new CheckResult { Status = CheckStatus.Failing, FailCount = 3 }));
    }
}

public class PermissionTests
{
    [Fact] public void Agent_accounts_never_pass() =>
        Assert.False(PermissionEvaluator.Has(new User { AgentId = 1, IsSuperuser = true }, Perm.ListAgents));

    [Fact] public void Empty_role_denied() => Assert.False(PermissionEvaluator.Has(new User(), Perm.ListAgents));

    [Fact]
    public void Client_scope_is_enforced()
    {
        var u = new User { Role = new Role { Permissions = [Perm.ListAgents], CanViewClients = [new Client { Id = 10 }] } };
        Assert.True(PermissionEvaluator.CanSeeSite(u, new Site { Id = 1, ClientId = 10 }));
        Assert.False(PermissionEvaluator.CanSeeSite(u, new Site { Id = 2, ClientId = 20 }));
    }
}

public class MiscTests
{
    [Fact] public void AgentId_rules() { Assert.False(AgentIdValidator.IsValid("short")); Assert.True(AgentIdValidator.IsValid(new string('a', 40))); }

    [Fact]
    public void Config_uses_snake_case_and_ranges()
    {
        var c = AgentConfigFactory.Create(new AgentRuntimeOptions());
        Assert.InRange(c.CheckinHello, 30, 60);
        Assert.Contains("\"checkin_hello\"", System.Text.Json.JsonSerializer.Serialize(c));
    }

    [Fact]
    public void Script_args_are_substituted()
    {
        var a = new Agent { Hostname = "PC1", Site = new Site { Name = "HQ", Client = new Client { Name = "Plaza" } } };
        Assert.Equal(["-h PC1", "Plaza/HQ"], ScriptArgParser.Parse(a, "powershell", ["-h {{agent.hostname}}", "{{client.name}}/{{site.name}}"]));
    }
}

public class MeshAndPatchTests
{
    private const string Key = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";

    [Fact]
    public void Mesh_auth_token_roundtrip_and_alphabet()
    {
        var t = MeshAuth.CreateAuthToken("Admin", Key, 1700000000);
        Assert.DoesNotContain('+', t); Assert.DoesNotContain('/', t);
        Assert.Equal("{\"userid\":\"user//admin\", \"domainid\":\"\", \"time\":1700000000}", MeshAuth.Decrypt(t, Key));
    }

    [Fact]
    public void Mesh_url_windows_vs_posix()
    {
        Assert.Equal("http://127.0.0.1:4430/meshagents?id=4&meshid=abc&installflags=0",
            MeshUrls.AgentDownload("http://127.0.0.1:4430/", MeshAgentIdent.Win64, AgentPlat.Windows, "abc"));
        Assert.Equal("https://m.example.com/meshagents?id=abc&installflags=2&meshinstall=10005",
            MeshUrls.AgentDownload("https://m.example.com", MeshAgentIdent.DarwinUniversal, AgentPlat.Darwin, "abc"));
    }

    [Fact]
    public void Mesh_ident_mapping()
    {
        Assert.Equal(4, MeshAgentIdent.ForInstaller(AgentPlat.Windows, GoArch.Amd64));
        Assert.Equal(3, MeshAgentIdent.ForInstaller(AgentPlat.Windows, GoArch.I386));
        Assert.Null(MeshAgentIdent.ForInstaller(AgentPlat.Linux, GoArch.Amd64));
    }

    [Theory]
    [InlineData("2.11.0", "2.12.0", true)]
    [InlineData("2.12.0", "2.12.0", false)]
    [InlineData("v2.13.1", "2.12.0", false)]
    [InlineData("1.0.0", "2.12.0-dev", false)]
    [InlineData("garbage", "2.12.0", true)]
    public void Installer_version(string v, string latest, bool outdated) => Assert.Equal(outdated, AgentVersion.IsInstallerOutdated(v, latest));

    [Fact]
    public void Superseded_keeps_latest_version_only()
    {
        var ids = SupersededUpdates.FindObsolete([
            (1, "KB1", "Feature update (Version 21H2)"),
            (2, "KB1", "Feature update (Version 22H2)"),
            (3, "KB1", "Feature update (Version 23H2)"),
            (4, "KB2", "Other without version"),
            (5, "KB2", "Other without version too"),
            (6, "KB3", "Single")]);
        Assert.Equal(new long[] { 1, 2 }, ids.OrderBy(x => x));
        Assert.DoesNotContain(3L, ids);
    }

    [Theory]
    [InlineData("always", false, true)]
    [InlineData("required", true, true)]
    [InlineData("required", false, false)]
    [InlineData("never", true, false)]
    [InlineData("inherit", true, false)]
    public void Reboot_decision(string eff, bool needs, bool expected) => Assert.Equal(expected, PatchPolicyResolver.ShouldReboot(eff, needs));

    [Fact]
    public void Agent_setting_overrides_policy() {
        Assert.Equal("never", PatchPolicyResolver.EffectiveReboot("never", "always"));
        Assert.Equal("always", PatchPolicyResolver.EffectiveReboot("inherit", "always"));
    }
}
