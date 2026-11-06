namespace TacticalRmm.Domain;

// Nilai string identik dengan Django (TextChoices) supaya agent Go & nats-api tetap kompatibel.
public static class AgentPlat { public const string Windows = "windows", Linux = "linux", Darwin = "darwin"; }
public static class GoArch { public const string Amd64 = "amd64", I386 = "386", Arm64 = "arm64", Arm32 = "arm"; }
public static class AgentMonType { public const string Server = "server", Workstation = "workstation"; }
public static class CheckType
{
    public const string DiskSpace = "diskspace", Ping = "ping", CpuLoad = "cpuload",
        Memory = "memory", WinSvc = "winsvc", Script = "script", EventLog = "eventlog";
}
public static class CheckStatus { public const string Passing = "passing", Failing = "failing", Pending = "pending"; }
public static class AlertSeverity { public const string Info = "info", Warning = "warning", Error = "error"; }
public static class AlertType { public const string Availability = "availability", Check = "check", Task = "task", Custom = "custom"; }
public static class TaskType
{
    public const string Daily = "daily", Weekly = "weekly", Monthly = "monthly", MonthlyDow = "monthlydow",
        CheckFailure = "checkfailure", Manual = "manual", RunOnce = "runonce", Onboarding = "onboarding";
}
public static class TaskRunStatus { public const string Running = "running", Completed = "completed"; }
public static class ScriptShell
{
    public const string PowerShell = "powershell", Cmd = "cmd", Python = "python",
        Shell = "shell", Nushell = "nushell", Deno = "deno";
}
public static class AuditAction
{
    public const string Login = "login", FailedLogin = "failed_login", Delete = "delete", Modify = "modify",
        Add = "add", View = "view", AgentInstall = "agent_install", ExecScript = "execute_script",
        ExecCommand = "execute_command", BulkAction = "bulk_action";
}

/// <summary>Pengganti puluhan flag boolean di Django Role; disimpan sebagai text[].</summary>
public static class Perm
{
    public const string ListClients = "can_list_clients", ManageClients = "can_manage_clients",
        ListAgents = "can_list_agents", ManageAgents = "can_manage_agents", UninstallAgents = "can_uninstall_agents",
        RebootAgents = "can_reboot_agents", RunScripts = "can_run_scripts", RunCommands = "can_send_cmd",
        ListScripts = "can_list_scripts", ManageScripts = "can_manage_scripts",
        ListChecks = "can_list_checks", ManageChecks = "can_manage_checks",
        ListTasks = "can_list_autotasks", ManageTasks = "can_manage_autotasks",
        ListAlerts = "can_list_alerts", ManageAlerts = "can_manage_alerts",
        ViewAuditLog = "can_view_auditlogs", ManageAccounts = "can_manage_accounts";
}
