using System.Text.Json;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using Xunit;

namespace TacticalRmm.Tests;

/// <summary>Pure logic for the dashboard CRUD: check/task validators, Task Scheduler payload, account rules.</summary>
public class CrudLogicTests
{
    private static readonly DateTime now = new(2026, 11, 7, 6, 0, 0, DateTimeKind.Utc);
    private static void T(string name, bool ok) => Assert.True(ok, name);

    [Fact]
    public void CheckValidator_Cases()
    {
            CheckUpsert C(Action<CheckUpsert> f) { var c = new CheckUpsert { FailsB4Alert = 1, RunInterval = 0 }; f(c); return c; }
            T("disk ok", CheckValidator.Validate(CheckType.DiskSpace, C(c => { c.Disk = "C:"; c.ErrorThreshold = 10; c.WarningThreshold = 20; })) is null);
            T("disk bad drive", CheckValidator.Validate(CheckType.DiskSpace, C(c => { c.Disk = "C:\\x"; c.ErrorThreshold = 10; })) is not null);
            T("disk warn must be > error", CheckValidator.Validate(CheckType.DiskSpace, C(c => { c.Disk = "C:"; c.ErrorThreshold = 20; c.WarningThreshold = 10; })) is not null);
            T("cpu warn must be < error", CheckValidator.Validate(CheckType.CpuLoad, C(c => { c.ErrorThreshold = 80; c.WarningThreshold = 90; })) is not null);
            T("cpu ok", CheckValidator.Validate(CheckType.CpuLoad, C(c => { c.ErrorThreshold = 90; c.WarningThreshold = 70; })) is null);
            T("cpu needs a threshold", CheckValidator.Validate(CheckType.CpuLoad, C(_ => { })) is not null);
            T("threshold > 99", CheckValidator.Validate(CheckType.Memory, C(c => c.ErrorThreshold = 150)) is not null);
            T("ping host ok", CheckValidator.Validate(CheckType.Ping, C(c => c.Ip = "8.8.8.8")) is null);
            T("ping hostname ok", CheckValidator.Validate(CheckType.Ping, C(c => c.Ip = "mail.example.co.id")) is null);
            T("ping rejects injection", CheckValidator.Validate(CheckType.Ping, C(c => c.Ip = "1.1.1.1; rm -rf /")) is not null);
            T("winsvc needs name", CheckValidator.Validate(CheckType.WinSvc, C(_ => { })) is not null);
            T("script needs timeout", CheckValidator.Validate(CheckType.Script, C(c => c.Script = 3)) is not null);
            T("script ok", CheckValidator.Validate(CheckType.Script, C(c => { c.Script = 3; c.Timeout = 90; })) is null);
            T("eventlog ok", CheckValidator.Validate(CheckType.EventLog, C(c => { c.LogName = "System"; c.SearchLastDays = 1; })) is null);
            T("unknown type", CheckValidator.Validate("nope", C(_ => { })) is not null);
            T("fails_b4_alert 0 invalid", CheckValidator.Validate(CheckType.Ping, C(c => { c.Ip = "1.1.1.1"; c.FailsB4Alert = 0; })) is not null);
            T("run_interval 5 invalid", CheckValidator.Validate(CheckType.Ping, C(c => { c.Ip = "1.1.1.1"; c.RunInterval = 5; })) is not null);
            T("default name disk", CheckValidator.DefaultName(CheckType.DiskSpace, new CheckUpsert { Disk = "D:" }, null) == "Disk Space Drive D: - Free Space Below Threshold");

    }

    [Fact]
    public void Actions_Cases()
    {
            JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();
            T("actions empty", TaskValidator.ValidateActions(J("[]")) is not null);
            T("actions cmd ok", TaskValidator.ValidateActions(J("[{\"type\":\"cmd\",\"command\":\"dir\",\"shell\":\"cmd\",\"timeout\":30}]")) is null);
            T("actions script ok", TaskValidator.ValidateActions(J("[{\"type\":\"script\",\"script\":1,\"script_args\":[],\"timeout\":30}]")) is null);
            T("actions script missing args", TaskValidator.ValidateActions(J("[{\"type\":\"script\",\"script\":1,\"timeout\":30}]")) is not null);
            T("actions bad shell", TaskValidator.ValidateActions(J("[{\"type\":\"cmd\",\"command\":\"x\",\"shell\":\"zsh\",\"timeout\":30}]")) is not null);
            T("actions no timeout", TaskValidator.ValidateActions(J("[{\"type\":\"cmd\",\"command\":\"x\",\"shell\":\"cmd\"}]")) is not null);
            T("actions unknown type", TaskValidator.ValidateActions(J("[{\"type\":\"evil\",\"timeout\":1}]")) is not null);
            T("actions not array", TaskValidator.ValidateActions(J("{}")) is not null);

    }

    [Fact]
    public void TaskValidator_Cases()
    {
            TaskUpsert Tk(Action<TaskUpsert> f) { var t = new TaskUpsert { Name = "n" }; f(t); return t; }
            T("manual ok", TaskValidator.Validate(Tk(_ => { }), TaskType.Manual, now) is null);
            T("onboarding rejected", TaskValidator.Validate(Tk(_ => { }), TaskType.Onboarding, now) is not null);
            T("daily needs date", TaskValidator.Validate(Tk(t => t.DailyInterval = 1), TaskType.Daily, now) is not null);
            T("daily ok", TaskValidator.Validate(Tk(t => { t.RunTimeDate = now; t.DailyInterval = 1; }), TaskType.Daily, now) is null);
            T("weekly needs weekdays", TaskValidator.Validate(Tk(t => { t.RunTimeDate = now; t.WeeklyInterval = 1; }), TaskType.Weekly, now) is not null);
            T("weekly ok", TaskValidator.Validate(Tk(t => { t.RunTimeDate = now; t.WeeklyInterval = 1; t.RunTimeBitWeekdays = 5; }), TaskType.Weekly, now) is null);
            T("monthly ok", TaskValidator.Validate(Tk(t => { t.RunTimeDate = now; t.MonthlyMonthsOfYear = 4095; t.MonthlyDaysOfMonth = 1; }), TaskType.Monthly, now) is null);
            T("monthlydow ok", TaskValidator.Validate(Tk(t => { t.RunTimeDate = now; t.RunTimeBitWeekdays = 2; t.MonthlyMonthsOfYear = 1; t.MonthlyWeeksOfMonth = 1; }), TaskType.MonthlyDow, now) is null);
            T("checkfailure needs check", TaskValidator.Validate(Tk(_ => { }), TaskType.CheckFailure, now) is not null);
            T("expire in past", TaskValidator.Validate(Tk(t => { t.RunTimeDate = now; t.DailyInterval = 1; t.ExpireDate = now.AddDays(-1); }), TaskType.Daily, now) is not null);
            T("bad duration", TaskValidator.Validate(Tk(t => { t.TaskRepetitionInterval = "abc"; }), TaskType.Manual, now) is not null);
            T("name required", TaskValidator.Validate(new TaskUpsert(), TaskType.Manual, now) is not null);

    }

    [Fact]
    public void PayloadBuilder_Cases()
    {
            T("iso 30m", TaskPayloadBuilder.ToIsoDuration("30m") == "PT30M");
            T("iso 1d (port Django)", TaskPayloadBuilder.ToIsoDuration("1d") == "P1DT");
            var manual = TaskPayloadBuilder.Build(new AutomatedTask { Id = 7, WinTaskName = "TacticalRMM_x", TaskType = TaskType.CheckFailure }, now);
            T("payload checkfailure => manual trigger", (string)manual["trigger"]! == "manual" && (bool)manual["start_when_available"]! == false && !manual.ContainsKey("start_year"));
            var weekly = TaskPayloadBuilder.Build(new AutomatedTask { Id = 8, TaskType = TaskType.Weekly, RunTimeDate = now, WeeklyInterval = 2, RunTimeBitWeekdays = 5, RunAsapAfterMissed = true }, now);
            T("payload weekly", (int)weekly["week_interval"]! == 2 && (int)weekly["days_of_week"]! == 5 && (int)weekly["start_hour"]! == 6 && (bool)weekly["start_when_available"]! == true);
            var monthlyLast = TaskPayloadBuilder.Build(new AutomatedTask { TaskType = TaskType.Monthly, RunTimeDate = now, MonthlyDaysOfMonth = 0x80000000L + 5, MonthlyMonthsOfYear = 7 }, now);
            T("payload monthly last day", (bool)monthlyLast["run_on_last_day_of_month"]! && (long)monthlyLast["days_of_month"]! == 5);
            var rep = TaskPayloadBuilder.Build(new AutomatedTask { TaskType = TaskType.Daily, RunTimeDate = now, DailyInterval = 1, TaskRepetitionInterval = "15m", TaskRepetitionDuration = "1h", StopTaskAtDurationEnd = true, ExpireDate = now.AddDays(3), RemoveIfNotScheduled = true }, now);
            T("payload repetition+expire", (string)rep["repetition_interval"]! == "PT15M" && (string)rep["repetition_duration"]! == "PT1H" && (int)rep["expire_day"]! == 10 && (bool)rep["delete_expired_task_after"]! == true);
            var runonce = TaskPayloadBuilder.Build(new AutomatedTask { TaskType = TaskType.RunOnce, RunTimeDate = now }, now);
            T("payload runonce start_when_available", (bool)runonce["start_when_available"]! == true);

    }

    [Fact]
    public void AccountRules_Cases()
    {
            T("username ok", AccountRules.ValidateUsername("arie.s") is null);
            T("username bad", AccountRules.ValidateUsername("a b") is not null && AccountRules.ValidateUsername("ab") is not null);
            T("pw short", AccountRules.ValidatePassword("Abc123!", "arie") is not null);
            T("pw contains username", AccountRules.ValidatePassword("Arie-Password1!", "arie") is not null);
            T("pw weak classes", AccountRules.ValidatePassword("abcdefghijklmnop", "bob") is not null);
            T("pw ok", AccountRules.ValidatePassword("Correct-Horse-9", "bob") is null);
            var sup = new User { IsSuperuser = true };
            var admin = new User { Role = new Role { Permissions = [Perm.ManageAccounts, Perm.ListAgents] } };
            T("admin cannot manage superuser", !AccountRules.CanManageUser(admin, sup));
            T("super can manage anyone", AccountRules.CanManageUser(sup, admin));
            T("admin can manage normal", AccountRules.CanManageUser(admin, new User()));
            T("admin cannot grant superuser role", !AccountRules.CanGrant(admin, true, []));
            T("admin cannot grant perms it lacks", !AccountRules.CanGrant(admin, false, [Perm.RunScripts]));
            T("admin can grant subset", AccountRules.CanGrant(admin, false, [Perm.ListAgents]));
            T("super can grant all", AccountRules.CanGrant(sup, true, Perm.All));
            T("role unknown perm", AccountRules.ValidateRole("x", ["can_do_anything"]) is not null);
            T("role ok", AccountRules.ValidateRole("Helpdesk", [Perm.ListAgents]) is null);
            T("role needs name", AccountRules.ValidateRole(" ", []) is not null);
            T("Perm.All has no duplicates", Perm.All.Distinct().Count() == Perm.All.Count);
            T("WinTaskName format", AutomatedTask.GenerateWinTaskName() is { Length: 47 } n && n.StartsWith("TacticalRMM_") && n[12..].All(char.IsLetter));
            T("WinTaskName unique", AutomatedTask.GenerateWinTaskName() != AutomatedTask.GenerateWinTaskName());
    }

}