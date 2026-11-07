namespace TacticalRmm.Domain.Entities;

public class Script : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Shell { get; set; } = ScriptShell.PowerShell;
    public List<string> Args { get; set; } = new();
    public List<string> EnvVars { get; set; } = new();
    public string ScriptBody { get; set; } = "";
    public int DefaultTimeout { get; set; } = 90;
    public bool RunAsUser { get; set; }
    public bool Hidden { get; set; }
    public List<string> SupportedPlatforms { get; set; } = new();
}

public class Check : AuditableEntity
{
    public int Id { get; set; }
    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }
    public int? PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public bool OverriddenByPolicy { get; set; }
    public string? Name { get; set; }
    public string CheckType { get; set; } = Domain.CheckType.DiskSpace;
    public bool EmailAlert { get; set; }
    public bool TextAlert { get; set; }
    public bool DashboardAlert { get; set; }
    public int FailsB4Alert { get; set; } = 1;
    public int RunInterval { get; set; }
    public string? AlertSeverity { get; set; } = Domain.AlertSeverity.Warning;
    public int? ErrorThreshold { get; set; }
    public int? WarningThreshold { get; set; }
    public string? Disk { get; set; }
    public string? Ip { get; set; }
    public int? ScriptId { get; set; }
    public Script? Script { get; set; }
    public List<string> ScriptArgs { get; set; } = new();
    public List<string> EnvVars { get; set; } = new();
    public List<long> InfoReturnCodes { get; set; } = new();
    public List<long> WarningReturnCodes { get; set; } = new();
    public List<long> SuccessReturnCodes { get; set; } = new();
    public int? Timeout { get; set; }
    public string? SvcName { get; set; }
    public string? SvcDisplayName { get; set; }
    public bool? PassIfStartPending { get; set; }
    public bool PassIfSvcNotExist { get; set; }
    public bool? RestartIfStopped { get; set; }
    public string? LogName { get; set; }
    public int? EventId { get; set; }
    public string? EventType { get; set; }
    public string? FailWhen { get; set; }
    public int? SearchLastDays { get; set; }
}

public class CheckResult
{
    public long Id { get; set; }
    public int AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public int CheckId { get; set; }
    public Check Check { get; set; } = null!;
    public string Status { get; set; } = CheckStatus.Pending;
    public string? AlertSeverity { get; set; }
    public string? MoreInfo { get; set; }
    public DateTime? LastRun { get; set; }
    public int FailCount { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public long? Retcode { get; set; }
    public string? ExecutionTime { get; set; }
    public List<int> History { get; set; } = new(); // 15 sampel terakhir (cpu/mem)
}

public class AutomatedTask : AuditableEntity
{
    public int Id { get; set; }
    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }
    public int? PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public int? AssignedCheckId { get; set; }
    public Check? AssignedCheck { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool ContinueOnError { get; set; } = true;
    public string AlertSeverity { get; set; } = Domain.AlertSeverity.Info;
    public bool EmailAlert { get; set; }
    public bool TextAlert { get; set; }
    public bool DashboardAlert { get; set; }
    public string TaskType { get; set; } = Domain.TaskType.Manual;
    /// <summary>Nama task di Windows Task Scheduler pada agent (unik).</summary>
    public string WinTaskName { get; set; } = GenerateWinTaskName();
    public string Actions { get; set; } = "[]"; // jsonb: [{type:"cmd"|"script", ...}]
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
    public bool StopTaskAtDurationEnd { get; set; }
    public string? RandomTaskDelay { get; set; }
    public bool RemoveIfNotScheduled { get; set; }
    public bool RunAsapAfterMissed { get; set; }
    public int TaskInstancePolicy { get; set; } = 1;
    public bool ManagedByPolicy { get; set; }
    /// <summary>Penghapusan menunggu konfirmasi agent (delschedtask); baris dihapus oleh TaskSyncJob.</summary>
    public bool PendingDeletion { get; set; }

    public static string GenerateWinTaskName()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return "TacticalRMM_" + string.Create(35, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }
}

public class TaskResult
{
    public long Id { get; set; }
    public int AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public int TaskId { get; set; }
    public AutomatedTask Task { get; set; } = null!;
    public long? Retcode { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public string ExecutionTime { get; set; } = "0.0000";
    public DateTime? LastRun { get; set; }
    public string Status { get; set; } = CheckStatus.Pending;
    public string RunStatus { get; set; } = TaskRunStatus.Completed;
    public string SyncStatus { get; set; } = TaskSyncStatus.Initial;
}

public class Alert
{
    public int Id { get; set; }
    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }
    public int? AssignedCheckId { get; set; }
    public int? AssignedTaskId { get; set; }
    public string AlertType { get; set; } = Domain.AlertType.Availability;
    public string? Message { get; set; }
    public DateTime AlertTime { get; set; }
    public bool Snoozed { get; set; }
    public DateTime? SnoozeUntil { get; set; }
    public bool Resolved { get; set; }
    public DateTime? ResolvedOn { get; set; }
    public string Severity { get; set; } = Domain.AlertSeverity.Info;
    public bool Hidden { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? Agent { get; set; }
    public int? AgentId { get; set; }
    public DateTime EntryTime { get; set; }
    public string Action { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string? Message { get; set; }
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public string? DebugInfo { get; set; }
}
