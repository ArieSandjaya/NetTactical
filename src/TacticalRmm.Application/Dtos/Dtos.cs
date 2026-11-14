using System.Text.Json;
using System.Text.Json.Serialization;
using TacticalRmm.Application.Logic;

namespace TacticalRmm.Application.Dtos;

public record LoginRequest(string Username, string Password, string? TwoFactor = null);
public record LoginResult(string? Token, DateTime? Expiry, bool TotpRequired, string? Error);

public record NewAgentRequest(
    [property: JsonPropertyName("agent_id")] string AgentId,
    string Hostname, int Site,
    [property: JsonPropertyName("monitoring_type")] string MonitoringType,
    string? Description,
    [property: JsonPropertyName("mesh_node_id")] string? MeshNodeId,
    string? Goarch, string Plat);
public record NewAgentResult(int Pk, string Token);

public record AgentListItem(int Id, string AgentId, string Hostname, string Plat, string? OperatingSystem,
    string Version, DateTime? LastSeen, string? PublicIp, string MonitoringType, string SiteName, string ClientName,
    bool MaintenanceMode, bool NeedsReboot, string? LoggedInUsername, string Status);

public record RunScriptRequest(int Script, string Output, List<string>? Args, bool RunAsUser,
    List<string>? EnvVars, int Timeout);

// ---------- Agent protocol (apiv3), field names = snake_case exactly as in Django ----------
public class AgentCheckInConfig
{
    [JsonPropertyName("checkin_hello")] public int CheckinHello { get; set; }
    [JsonPropertyName("checkin_agentinfo")] public int CheckinAgentInfo { get; set; }
    [JsonPropertyName("checkin_winsvc")] public int CheckinWinSvc { get; set; }
    [JsonPropertyName("checkin_pubip")] public int CheckinPubIp { get; set; }
    [JsonPropertyName("checkin_disks")] public int CheckinDisks { get; set; }
    [JsonPropertyName("checkin_sw")] public int CheckinSw { get; set; }
    [JsonPropertyName("checkin_wmi")] public int CheckinWmi { get; set; }
    [JsonPropertyName("checkin_syncmesh")] public int CheckinSyncMesh { get; set; }
    [JsonPropertyName("limit_data")] public bool LimitData { get; set; }
    [JsonPropertyName("install_nushell")] public bool InstallNushell { get; set; }
    [JsonPropertyName("install_nushell_version")] public string InstallNushellVersion { get; set; } = "";
    [JsonPropertyName("install_nushell_url")] public string InstallNushellUrl { get; set; } = "";
    [JsonPropertyName("nushell_enable_config")] public bool NushellEnableConfig { get; set; }
    [JsonPropertyName("install_deno")] public bool InstallDeno { get; set; }
    [JsonPropertyName("install_deno_version")] public string InstallDenoVersion { get; set; } = "";
    [JsonPropertyName("install_deno_url")] public string InstallDenoUrl { get; set; } = "";
    [JsonPropertyName("deno_default_permissions")] public string DenoDefaultPermissions { get; set; } = "";
}

public class IntRange
{
    public int Min { get; set; }
    public int Max { get; set; }
    public IntRange() { }
    public IntRange(int min, int max) { Min = min; Max = max; }
}

public class AgentRuntimeOptions
{
    public IntRange Hello { get; set; } = new(30, 60);
    public IntRange AgentInfo { get; set; } = new(200, 400);
    public IntRange WinSvc { get; set; } = new(2400, 3000);
    public IntRange PubIp { get; set; } = new(300, 500);
    public IntRange Disks { get; set; } = new(1000, 2000);
    public IntRange Sw { get; set; } = new(2800, 3500);
    public IntRange Wmi { get; set; } = new(3000, 4000);
    public IntRange SyncMesh { get; set; } = new(800, 1200);
    public bool LimitData { get; set; }
    public bool InstallNushell { get; set; } = true;
    public string NushellVersion { get; set; } = "0.116.1";
    public string NushellUrl { get; set; } = "";
    public bool NushellEnableConfig { get; set; }
    public bool InstallDeno { get; set; } = true;
    public string DenoVersion { get; set; } = "v1.46.3";
    public string DenoUrl { get; set; } = "";
    public string DenoDefaultPermissions { get; set; } = "--allow-all";
}

/// <summary>The "Tactical" configuration section in appsettings / environment.</summary>
public class TacticalOptions
{
    public string SecretKey { get; set; } = "";                 // also the password of the NATS user "tacticalrmm"
    public string NatsUrl { get; set; } = "nats://127.0.0.1:4222";
    public string NatsConfPath { get; set; } = "nats-rmm.conf";
    public string NatsServerBin { get; set; } = "/usr/local/bin/nats-server";
    public string NatsStdHost { get; set; } = "0.0.0.0";
    public int NatsStdPort { get; set; } = 4222;
    public string NatsWsHost { get; set; } = "127.0.0.1";
    public int NatsWsPort { get; set; } = 9235;
    public string AllowResponseExpiration { get; set; } = "1435m";
    public int UserTokenTtlHours { get; set; } = 5;
    public string LatestAgentVer { get; set; } = "2.12.0";
    public int MaxRequestSizeBytes { get; set; } = 10 * 1024 * 1024; // = TRMM_MAX_REQUEST_SIZE
    public AgentRuntimeOptions Agent { get; set; } = new();
    /// <summary>How many days historical metrics are kept before the retention job deletes them.</summary>
    public int MetricRetentionDays { get; set; } = 90;
    /// <summary>Minimum gap between metric points per check (seconds), to keep the table from bloating.</summary>
    public int MetricMinIntervalSeconds { get; set; } = 240;
    public HealthWeights Health { get; set; } = new();
    public VncOptions Vnc { get; set; } = new();
}

public class VncOptions
{
    public int Port { get; set; } = 5900;
    /// <summary>Per-attempt connect/handshake timeout.</summary>
    public int TimeoutMs { get; set; } = 2500;
    /// <summary>Upper bound on password attempts per connect (VNC servers such as TightVNC temporarily block a client after repeated failures).</summary>
    public int MaxAttempts { get; set; } = 4;
    /// <summary>Default false: only private LAN addresses are ever contacted.</summary>
    public bool AllowPublicTargets { get; set; }
}

public class CheckRunnerItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("agent")] public int? Agent { get; set; }
    [JsonPropertyName("check_type")] public string CheckType { get; set; } = "";
    [JsonPropertyName("run_interval")] public int RunInterval { get; set; }
    [JsonPropertyName("alert_severity")] public string? AlertSeverity { get; set; }
    [JsonPropertyName("error_threshold")] public int? ErrorThreshold { get; set; }
    [JsonPropertyName("warning_threshold")] public int? WarningThreshold { get; set; }
    [JsonPropertyName("disk")] public string? Disk { get; set; }
    [JsonPropertyName("ip")] public string? Ip { get; set; }
    [JsonPropertyName("script")] public ScriptCheckDto? Script { get; set; }
    [JsonPropertyName("script_args")] public List<string> ScriptArgs { get; set; } = new();
    [JsonPropertyName("env_vars")] public List<string> EnvVars { get; set; } = new();
    [JsonPropertyName("timeout")] public int? Timeout { get; set; }
    [JsonPropertyName("svc_name")] public string? SvcName { get; set; }
    [JsonPropertyName("pass_if_start_pending")] public bool? PassIfStartPending { get; set; }
    [JsonPropertyName("pass_if_svc_not_exist")] public bool PassIfSvcNotExist { get; set; }
    [JsonPropertyName("restart_if_stopped")] public bool? RestartIfStopped { get; set; }
    [JsonPropertyName("log_name")] public string? LogName { get; set; }
    [JsonPropertyName("event_id")] public int? EventId { get; set; }
    [JsonPropertyName("event_type")] public string? EventType { get; set; }
    [JsonPropertyName("fail_when")] public string? FailWhen { get; set; }
    [JsonPropertyName("search_last_days")] public int? SearchLastDays { get; set; }
}
public record ScriptCheckDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("shell")] string Shell,
    [property: JsonPropertyName("run_as_user")] bool RunAsUser);

public record CheckRunnerResponse(
    [property: JsonPropertyName("agent")] int Agent,
    [property: JsonPropertyName("check_interval")] int CheckInterval,
    [property: JsonPropertyName("checks")] List<CheckRunnerItem> Checks);

/// <summary>PATCH checkrunner payload from the agent; optional fields depend on the check type.</summary>
public class CheckResultPayload
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("agent_id")] public string? AgentId { get; set; }
    [JsonPropertyName("percent")] public int? Percent { get; set; }
    [JsonPropertyName("exists")] public bool? Exists { get; set; }
    [JsonPropertyName("percent_used")] public double? PercentUsed { get; set; }
    [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    [JsonPropertyName("stdout")] public string? Stdout { get; set; }
    [JsonPropertyName("stderr")] public string? Stderr { get; set; }
    [JsonPropertyName("retcode")] public long? Retcode { get; set; }
    [JsonPropertyName("runtime")] public double? Runtime { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("output")] public string? Output { get; set; }
}

public class TaskActionDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("command")] public string? Command { get; set; }
    [JsonPropertyName("script_name")] public string? ScriptName { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("script_args")] public List<string>? ScriptArgs { get; set; }
    [JsonPropertyName("shell")] public string? Shell { get; set; }
    [JsonPropertyName("timeout")] public int Timeout { get; set; }
    [JsonPropertyName("run_as_user")] public bool? RunAsUser { get; set; }
    [JsonPropertyName("env_vars")] public List<string>? EnvVars { get; set; }
    [JsonPropertyName("nushell_enable_config")] public bool? NushellEnableConfig { get; set; }
    [JsonPropertyName("deno_default_permissions")] public string? DenoDefaultPermissions { get; set; }
}
public record TaskRunnerResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("continue_on_error")] bool ContinueOnError,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("task_actions")] List<TaskActionDto> TaskActions);

public class TaskResultPayload
{
    [JsonPropertyName("retcode")] public long? Retcode { get; set; }
    [JsonPropertyName("stdout")] public string? Stdout { get; set; }
    [JsonPropertyName("stderr")] public string? Stderr { get; set; }
    [JsonPropertyName("runtime")] public double? Runtime { get; set; }
    [JsonPropertyName("execution_time")] public string? ExecutionTime { get; set; }
}

public class WinUpdateItem
{
    [JsonPropertyName("guid")] public string Guid { get; set; } = "";
    [JsonPropertyName("kb_article_ids")] public List<string>? KbArticleIds { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("installed")] public bool Installed { get; set; }
    [JsonPropertyName("downloaded")] public bool Downloaded { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
    [JsonPropertyName("categories")] public List<string>? Categories { get; set; }
    [JsonPropertyName("category_ids")] public List<string>? CategoryIds { get; set; }
    [JsonPropertyName("more_info_urls")] public List<string>? MoreInfoUrls { get; set; }
    [JsonPropertyName("support_url")] public string? SupportUrl { get; set; }
    [JsonPropertyName("revision_number")] public int? RevisionNumber { get; set; }
}
public class WinUpdatesPost { [JsonPropertyName("wua_updates")] public List<WinUpdateItem>? WuaUpdates { get; set; } }
public class WinUpdatesPut { [JsonPropertyName("needs_reboot")] public bool NeedsReboot { get; set; } }
public class WinUpdatesPatch
{
    [JsonPropertyName("guid")] public string Guid { get; set; } = "";
    [JsonPropertyName("success")] public bool Success { get; set; }
}
public class GuidPayload { [JsonPropertyName("guid")] public string Guid { get; set; } = ""; }
public class SyncMeshPayload
{
    [JsonPropertyName("nodeid")] public string NodeId { get; set; } = "";
    [JsonPropertyName("run_sync_task")] public bool? RunSyncTask { get; set; }
}
public class ChocoPayload { [JsonPropertyName("installed")] public bool Installed { get; set; } }
public class InstallerVersionPayload { [JsonPropertyName("version")] public string? Version { get; set; } }
public class MeshExePayload
{
    [JsonPropertyName("goarch")] public string? Goarch { get; set; }
    [JsonPropertyName("plat")] public string? Plat { get; set; }
}
