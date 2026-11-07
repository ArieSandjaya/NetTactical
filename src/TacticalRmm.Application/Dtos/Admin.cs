using System.Text.Json;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Dtos;

public enum OutcomeStatus { Ok, NotFound, Forbidden, Invalid, Conflict }

public record Outcome<T>(OutcomeStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == OutcomeStatus.Ok;
}

public static class Outcome
{
    public static Outcome<T> Ok<T>(T value) => new(OutcomeStatus.Ok, value, null);
    public static Outcome<T> NotFound<T>(string? msg = null) => new(OutcomeStatus.NotFound, default, msg ?? "Not found");
    public static Outcome<T> Forbidden<T>(string? msg = null) => new(OutcomeStatus.Forbidden, default, msg ?? "Forbidden");
    public static Outcome<T> Invalid<T>(string msg) => new(OutcomeStatus.Invalid, default, msg);
    public static Outcome<T> Conflict<T>(string msg) => new(OutcomeStatus.Conflict, default, msg);
}

// ----------------------------------------------------------------------------- checks
public class CheckUpsert
{
    public string? Agent { get; set; }          // agent_id (string), bukan pk — sama dengan Django
    public int? Policy { get; set; }
    public string? CheckType { get; set; }      // hanya saat create
    public string? Name { get; set; }
    public bool? EmailAlert { get; set; }
    public bool? TextAlert { get; set; }
    public bool? DashboardAlert { get; set; }
    public int? FailsB4Alert { get; set; }
    public int? RunInterval { get; set; }
    public string? AlertSeverity { get; set; }
    public int? ErrorThreshold { get; set; }
    public int? WarningThreshold { get; set; }
    public string? Disk { get; set; }
    public string? Ip { get; set; }
    public int? Script { get; set; }
    public List<string>? ScriptArgs { get; set; }
    public List<string>? EnvVars { get; set; }
    public List<long>? InfoReturnCodes { get; set; }
    public List<long>? WarningReturnCodes { get; set; }
    public List<long>? SuccessReturnCodes { get; set; }
    public int? Timeout { get; set; }
    public string? SvcName { get; set; }
    public string? SvcDisplayName { get; set; }
    public bool? PassIfStartPending { get; set; }
    public bool? PassIfSvcNotExist { get; set; }
    public bool? RestartIfStopped { get; set; }
    public string? LogName { get; set; }
    public int? EventId { get; set; }
    public string? EventType { get; set; }
    public string? FailWhen { get; set; }
    public int? SearchLastDays { get; set; }
}

public record CheckDto(
    int Id, string? Agent, int? Policy, string CheckType, string? Name, bool EmailAlert, bool TextAlert, bool DashboardAlert,
    int FailsB4Alert, int RunInterval, string? AlertSeverity, int? ErrorThreshold, int? WarningThreshold, string? Disk, string? Ip,
    int? Script, List<string> ScriptArgs, List<string> EnvVars, List<long> InfoReturnCodes, List<long> WarningReturnCodes,
    List<long> SuccessReturnCodes, int? Timeout, string? SvcName, string? SvcDisplayName, bool? PassIfStartPending,
    bool PassIfSvcNotExist, bool? RestartIfStopped, string? LogName, int? EventId, string? EventType, string? FailWhen, int? SearchLastDays,
    string? Status, DateTime? LastRun, string? MoreInfo, string? Stdout, string? Stderr, long? Retcode, string? ExecutionTime,
    bool ManagedByPolicy)
{
    public static CheckDto From(Check c, string? agentPublicId, CheckResult? r, bool managedByPolicy) => new(
        c.Id, agentPublicId, c.PolicyId, c.CheckType, c.Name, c.EmailAlert, c.TextAlert, c.DashboardAlert, c.FailsB4Alert, c.RunInterval,
        c.AlertSeverity, c.ErrorThreshold, c.WarningThreshold, c.Disk, c.Ip, c.ScriptId, c.ScriptArgs, c.EnvVars, c.InfoReturnCodes,
        c.WarningReturnCodes, c.SuccessReturnCodes, c.Timeout, c.SvcName, c.SvcDisplayName, c.PassIfStartPending, c.PassIfSvcNotExist,
        c.RestartIfStopped, c.LogName, c.EventId, c.EventType, c.FailWhen, c.SearchLastDays,
        r?.Status ?? "pending", r?.LastRun, r?.MoreInfo, r?.Stdout, r?.Stderr, r?.Retcode, r?.ExecutionTime, managedByPolicy);
}

// ----------------------------------------------------------------------------- tasks
public class TaskUpsert
{
    public string? Agent { get; set; }
    public int? Policy { get; set; }
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
    public bool? ContinueOnError { get; set; }
    public string? AlertSeverity { get; set; }
    public bool? EmailAlert { get; set; }
    public bool? TextAlert { get; set; }
    public bool? DashboardAlert { get; set; }
    public string? TaskType { get; set; }
    public DateTime? RunTimeDate { get; set; }
    public DateTime? ExpireDate { get; set; }
    public int? DailyInterval { get; set; }
    public int? WeeklyInterval { get; set; }
    public int? RunTimeBitWeekdays { get; set; }
    public long? MonthlyDaysOfMonth { get; set; }
    public int? MonthlyMonthsOfYear { get; set; }
    public int? MonthlyWeeksOfMonth { get; set; }
    public string? TaskRepetitionDuration { get; set; }
    public string? TaskRepetitionInterval { get; set; }
    public bool? StopTaskAtDurationEnd { get; set; }
    public string? RandomTaskDelay { get; set; }
    public bool? RemoveIfNotScheduled { get; set; }
    public bool? RunAsapAfterMissed { get; set; }
    public int? TaskInstancePolicy { get; set; }
    public int? AssignedCheck { get; set; }
    public JsonElement Actions { get; set; }   // Undefined = tidak dikirim
}

public record TaskDto(
    int Id, string? Agent, int? Policy, string Name, bool Enabled, bool ContinueOnError, string AlertSeverity, bool EmailAlert,
    bool TextAlert, bool DashboardAlert, string TaskType, string WinTaskName, JsonElement Actions, DateTime? RunTimeDate, DateTime? ExpireDate,
    int? DailyInterval, int? WeeklyInterval, int? RunTimeBitWeekdays, long? MonthlyDaysOfMonth, int? MonthlyMonthsOfYear,
    int? MonthlyWeeksOfMonth, string? TaskRepetitionDuration, string? TaskRepetitionInterval, bool StopTaskAtDurationEnd,
    string? RandomTaskDelay, bool RemoveIfNotScheduled, bool RunAsapAfterMissed, int TaskInstancePolicy, int? AssignedCheck,
    string? Status, DateTime? LastRun, string? SyncStatus, bool PendingDeletion)
{
    public static TaskDto From(AutomatedTask t, string? agentPublicId, TaskResult? r) => new(
        t.Id, agentPublicId, t.PolicyId, t.Name, t.Enabled, t.ContinueOnError, t.AlertSeverity, t.EmailAlert, t.TextAlert, t.DashboardAlert,
        t.TaskType, t.WinTaskName, SafeJson(t.Actions), t.RunTimeDate, t.ExpireDate, t.DailyInterval, t.WeeklyInterval, t.RunTimeBitWeekdays,
        t.MonthlyDaysOfMonth, t.MonthlyMonthsOfYear, t.MonthlyWeeksOfMonth, t.TaskRepetitionDuration, t.TaskRepetitionInterval,
        t.StopTaskAtDurationEnd, t.RandomTaskDelay, t.RemoveIfNotScheduled, t.RunAsapAfterMissed, t.TaskInstancePolicy, t.AssignedCheckId,
        r?.Status ?? "pending", r?.LastRun, r?.SyncStatus, t.PendingDeletion);

    private static JsonElement SafeJson(string raw)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "[]" : raw).RootElement.Clone(); }
        catch (JsonException) { return JsonDocument.Parse("[]").RootElement.Clone(); }
    }
}

// ----------------------------------------------------------------------------- accounts
public record UserDto(int Id, string Username, string? Email, bool IsActive, bool IsSuperuser, bool BlockDashboardLogin,
    int? RoleId, string? RoleName, bool TotpEnabled, string? LastLoginIp);
public class UserCreate
{
    public string Username { get; set; } = "";
    public string? Email { get; set; }
    public string Password { get; set; } = "";
    public int? RoleId { get; set; }
    public bool IsSuperuser { get; set; }
    public bool BlockDashboardLogin { get; set; }
}
public class UserUpdate
{
    public string? Email { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsSuperuser { get; set; }
    public bool? BlockDashboardLogin { get; set; }
    public int? RoleId { get; set; }
    public bool? ClearRole { get; set; }
}
public class ResetPasswordRequest { public string Password { get; set; } = ""; }

public record RoleDto(int Id, string Name, bool IsSuperuser, List<string> Permissions, List<int> CanViewClients, List<int> CanViewSites);
public class RoleUpsert
{
    public string? Name { get; set; }
    public bool? IsSuperuser { get; set; }
    public List<string>? Permissions { get; set; }
    public List<int>? CanViewClients { get; set; }
    public List<int>? CanViewSites { get; set; }
}

// ----------------------------------------------------------------------------- org & policy
public class ClientUpdate { public string? Name { get; set; } }
public class SiteUpsert { public string? Name { get; set; } public int? Client { get; set; } }
public record SiteDto(int Id, string Name, int ClientId);
public class AgentUpdate
{
    public string? Description { get; set; }
    public string? MonitoringType { get; set; }
    public int? Site { get; set; }
    public bool? MaintenanceMode { get; set; }
    public int? CheckInterval { get; set; }
    public int? OfflineTime { get; set; }
    public int? OverdueTime { get; set; }
    public bool? OverdueEmailAlert { get; set; }
    public bool? OverdueTextAlert { get; set; }
    public bool? OverdueDashboardAlert { get; set; }
    public int? Policy { get; set; }
    public bool? ClearPolicy { get; set; }
    public bool? BlockPolicyInheritance { get; set; }
    public string? TimeZone { get; set; }
}
public class PolicyUpsert { public string? Name { get; set; } public string? Desc { get; set; } public bool? Active { get; set; } public bool? Enforced { get; set; } }
public record PolicyDto(int Id, string Name, string? Desc, bool Active, bool Enforced, int AgentCount, int CheckCount, int TaskCount);

public record TotpSetup(string Secret, string Uri);
public class TotpEnableRequest { public string Secret { get; set; } = ""; public string Code { get; set; } = ""; }
