using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Abstractions;

public interface ICheckAdminService
{
    Task<Outcome<IReadOnlyList<CheckDto>>> ListForAgentAsync(User caller, string agentId, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<CheckDto>>> ListForPolicyAsync(User caller, int policyId, CancellationToken ct = default);
    Task<Outcome<CheckDto>> GetAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<CheckDto>> CreateAsync(User caller, CheckUpsert req, CancellationToken ct = default);
    Task<Outcome<CheckDto>> UpdateAsync(User caller, int id, CheckUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface ITaskAdminService
{
    Task<Outcome<IReadOnlyList<TaskDto>>> ListForAgentAsync(User caller, string agentId, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<TaskDto>>> ListForPolicyAsync(User caller, int policyId, CancellationToken ct = default);
    Task<Outcome<TaskDto>> GetAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<TaskDto>> CreateAsync(User caller, TaskUpsert req, CancellationToken ct = default);
    Task<Outcome<TaskDto>> UpdateAsync(User caller, int id, TaskUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<bool>> RunNowAsync(User caller, int id, string? agentId, CancellationToken ct = default);
}

/// <summary>Pushes task definitions to the agent's Windows Task Scheduler (schedtask / delschedtask).</summary>
public interface ITaskSyncService
{
    Task<int> SyncPendingAsync(CancellationToken ct = default);
    Task SyncOneAsync(int taskId, int agentId, CancellationToken ct = default);
}

public interface IUserAdminService
{
    Task<Outcome<IReadOnlyList<UserDto>>> ListAsync(User caller, CancellationToken ct = default);
    Task<Outcome<UserDto>> GetAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<UserDto>> CreateAsync(User caller, UserCreate req, CancellationToken ct = default);
    Task<Outcome<UserDto>> UpdateAsync(User caller, int id, UserUpdate req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<bool>> ResetPasswordAsync(User caller, int id, string newPassword, CancellationToken ct = default);
    Task<Outcome<bool>> ResetTotpAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Creates a new TOTP secret for the user themselves (not saved until the code is verified).</summary>
    Task<Outcome<TotpSetup>> BeginTotpAsync(User caller, CancellationToken ct = default);
    Task<Outcome<bool>> EnableTotpAsync(User caller, string secret, string code, CancellationToken ct = default);
    /// <summary>Turns off the caller's own 2FA; requires the current password and a valid current code.</summary>
    Task<Outcome<bool>> DisableTotpAsync(User caller, string password, string code, CancellationToken ct = default);
}

public interface IRoleAdminService
{
    IReadOnlyList<string> AllPermissions { get; }
    Task<Outcome<IReadOnlyList<RoleDto>>> ListAsync(User caller, CancellationToken ct = default);
    Task<Outcome<RoleDto>> CreateAsync(User caller, RoleUpsert req, CancellationToken ct = default);
    Task<Outcome<RoleDto>> UpdateAsync(User caller, int id, RoleUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface IOrgAdminService
{
    Task<Outcome<bool>> UpdateClientAsync(User caller, int id, ClientUpdate req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteClientAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<SiteDto>> AddSiteAsync(User caller, SiteUpsert req, CancellationToken ct = default);
    Task<Outcome<SiteDto>> UpdateSiteAsync(User caller, int id, SiteUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteSiteAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<bool>> UpdateAgentAsync(User caller, string agentId, AgentUpdate req, CancellationToken ct = default);
}

public interface IPolicyAdminService
{
    Task<Outcome<IReadOnlyList<PolicyDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<PolicyDto>> CreateAsync(User caller, PolicyUpsert req, CancellationToken ct = default);
    Task<Outcome<PolicyDto>> UpdateAsync(User caller, int id, PolicyUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface IAlertTemplateAdminService
{
    Task<Outcome<IReadOnlyList<AlertTemplateDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<AlertTemplateDto>> CreateAsync(User caller, AlertTemplateUpsert req, CancellationToken ct = default);
    Task<Outcome<AlertTemplateDto>> UpdateAsync(User caller, int id, AlertTemplateUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface ICustomFieldService
{
    Task<Outcome<IReadOnlyList<CustomFieldDto>>> ListAsync(string? model, CancellationToken ct = default);
    Task<Outcome<CustomFieldDto>> CreateAsync(User caller, CustomFieldUpsert req, CancellationToken ct = default);
    Task<Outcome<CustomFieldDto>> UpdateAsync(User caller, int id, CustomFieldUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Every field of the model with this target's value. Target is the client or site id, or the agent_id string.</summary>
    Task<Outcome<IReadOnlyList<CustomFieldValueDto>>> ValuesAsync(User caller, string model, string target, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<CustomFieldValueDto>>> SetValuesAsync(User caller, string model, string target, CustomFieldValuesSet req, CancellationToken ct = default);
}

public interface ISnippetService
{
    Task<Outcome<IReadOnlyList<SnippetDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<SnippetDto>> GetAsync(int id, CancellationToken ct = default);
    Task<Outcome<SnippetDto>> CreateAsync(User caller, SnippetUpsert req, CancellationToken ct = default);
    Task<Outcome<SnippetDto>> UpdateAsync(User caller, int id, SnippetUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface IVncService
{
    Task<Outcome<IReadOnlyList<VncCredentialDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<VncCredentialDto>> CreateAsync(User caller, VncCredentialUpsert req, CancellationToken ct = default);
    Task<Outcome<VncCredentialDto>> UpdateAsync(User caller, int id, VncCredentialUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Checks reachability and which stored password works. Always Ok with a status, unless the asset is missing or out of scope.</summary>
    Task<Outcome<VncTestDto>> TestAsync(User caller, string agentId, CancellationToken ct = default);
    /// <summary>Same check, then builds the connection file. Conflict (with the reason) when it cannot connect.</summary>
    Task<Outcome<VncFileDto>> PrepareFileAsync(User caller, string agentId, CancellationToken ct = default);
}

public interface INotificationAdminService
{
    Task<Outcome<IReadOnlyList<NotificationChannelDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<NotificationChannelDto>> CreateAsync(User caller, NotificationChannelUpsert req, CancellationToken ct = default);
    Task<Outcome<NotificationChannelDto>> UpdateAsync(User caller, int id, NotificationChannelUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Sends a sample alert to one channel now and reports the result.</summary>
    Task<Outcome<NotificationTestResult>> TestAsync(User caller, int id, CancellationToken ct = default);
}

/// <summary>Hands alert events to the background sender; never blocks and never throws.</summary>
public interface INotificationQueue { void Enqueue(AlertEvent e); }

public interface IInstallerService
{
    /// <param name="defaultApi">Used when the request does not name the public API URL (the URL of the current request).</param>
    Task<Outcome<InstallerDto>> GenerateAsync(User caller, InstallerRequest req, string defaultApi, CancellationToken ct = default);
}

/// <summary>Runs a PowerShell script on a remote Windows computer over WinRM with explicit credentials.</summary>
public interface IRemoteExecutor
{
    Task<RemoteRunResult> RunAsync(RemoteRunRequest request, CancellationToken ct = default);
}

public interface IPushInstallService
{
    /// <summary>Validates, then starts a background push and returns at once; poll <see cref="GetAsync"/>.</summary>
    Task<Outcome<PushJobDto>> StartAsync(User caller, PushInstallRequest req, string defaultApi, CancellationToken ct = default);
    Task<Outcome<PushJobDto>> GetAsync(User caller, string id, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<PushJobDto>>> ListAsync(User caller, CancellationToken ct = default);
}

public interface IWinUpdateAdminService
{
    Task<Outcome<IReadOnlyList<WinUpdateDto>>> ListAsync(string agentId, CancellationToken ct = default);
    Task<Outcome<int>> SetActionAsync(User caller, string agentId, WinUpdateActionRequest req, CancellationToken ct = default);
    /// <summary>Asks the agent to scan Windows Update now; the result arrives through the normal agent callback.</summary>
    Task<Outcome<bool>> ScanAsync(User caller, string agentId, CancellationToken ct = default);
    /// <summary>Installs the approved, not yet installed updates now.</summary>
    Task<Outcome<WinUpdateInstallResult>> InstallNowAsync(User caller, string agentId, CancellationToken ct = default);
    Task<Outcome<WinUpdatePolicyView>> GetAgentPolicyAsync(string agentId, CancellationToken ct = default);
    Task<Outcome<WinUpdatePolicyView>> SaveAgentPolicyAsync(User caller, string agentId, WinUpdatePolicyDto req, CancellationToken ct = default);
    Task<Outcome<WinUpdatePolicyView>> GetPolicyAsync(int policyId, CancellationToken ct = default);
    Task<Outcome<WinUpdatePolicyView>> SavePolicyAsync(User caller, int policyId, WinUpdatePolicyDto req, CancellationToken ct = default);
}

public interface IBulkService
{
    /// <summary>Validates and checks permission and access to every asset, then runs in the background and returns at once; poll <see cref="GetAsync"/>.</summary>
    Task<Outcome<BulkJobDto>> StartAsync(User caller, BulkRequest req, CancellationToken ct = default);
    Task<Outcome<BulkJobDto>> GetAsync(User caller, string id, CancellationToken ct = default);
}

public interface IAgentLogService
{
    Task<Outcome<IReadOnlyList<HistoryEntryDto>>> HistoryAsync(string agentId, int? take, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<NoteDto>>> NotesAsync(string agentId, CancellationToken ct = default);
    Task<Outcome<NoteDto>> AddNoteAsync(User caller, string agentId, string? text, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteNoteAsync(User caller, string agentId, int noteId, CancellationToken ct = default);
    /// <summary>Adds a "cmd_run" line to the asset's history. Best effort: a problem here never stops the command.</summary>
    Task RecordCommandAsync(string agentId, string username, string command, CancellationToken ct = default);
}

/// <summary>Live look at an asset: Windows services and running processes, and the actions on them. Every call asks the agent through NATS.</summary>
public interface IRemoteToolsService
{
    Task<Outcome<IReadOnlyList<ServiceDto>>> ServicesAsync(string agentId, CancellationToken ct = default);
    Task<Outcome<ToolResult>> ServiceActionAsync(User caller, string agentId, ServiceActionRequest req, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<ProcessDto>>> ProcessesAsync(string agentId, CancellationToken ct = default);
    Task<Outcome<ToolResult>> KillAsync(User caller, string agentId, KillRequest req, CancellationToken ct = default);
}

/// <summary>Tells an agent to download and install the version set in Tactical:LatestAgentVer (the agent restarts itself).</summary>
public interface IAgentUpdateService
{
    Task<Outcome<AgentUpdateDto>> UpdateAsync(string username, string agentId, CancellationToken ct = default);
}

/// <summary>Browse and change files on a Windows asset by running small PowerShell scripts on its agent. Each step is audited.</summary>
public interface IFileBrowserService
{
    Task<Outcome<FileListDto>> ListAsync(string agentId, string? path, CancellationToken ct = default);
    Task<Outcome<FileDownloadDto>> DownloadAsync(User caller, string agentId, string? path, CancellationToken ct = default);
    Task<Outcome<ToolResult>> UploadAsync(User caller, string agentId, FileUploadRequest req, CancellationToken ct = default);
    Task<Outcome<ToolResult>> NewFolderAsync(User caller, string agentId, FolderRequest req, CancellationToken ct = default);
    Task<Outcome<ToolResult>> DeleteAsync(User caller, string agentId, FileDeleteRequest req, CancellationToken ct = default);
    Task<Outcome<ToolResult>> RenameAsync(User caller, string agentId, FileRenameRequest req, CancellationToken ct = default);
}

/// <summary>Reads the Windows event log of an asset live from its agent (Get-WinEvent through a script).</summary>
public interface IEventLogService
{
    Task<Outcome<EventLogDto>> ReadAsync(string agentId, string? log, string? level, int hours, int max, string? text, int? eventId, CancellationToken ct = default);
}

/// <summary>Shareable install links (deployments) and the public script they serve.</summary>
public interface IDeploymentService
{
    Task<Outcome<IReadOnlyList<DeploymentDto>>> ListAsync(User caller, CancellationToken ct = default);
    Task<Outcome<DeploymentDto>> CreateAsync(User caller, DeploymentRequest req, string defaultApi, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Public: the install script for a link, or NotFound when it is unknown, expired or used up (never says which).</summary>
    Task<Outcome<string>> ScriptAsync(string uid, CancellationToken ct = default);
}

/// <summary>Housekeeping the admin triggers by hand: prune old audit, alert, history and token rows, and remove assets that stopped reporting.</summary>
public interface IServerMaintenanceService
{
    Task<Outcome<MaintenanceResult>> PreviewAsync(string? action, int days, CancellationToken ct = default);
    Task<Outcome<MaintenanceResult>> RunAsync(User caller, string? action, int days, CancellationToken ct = default);
}

/// <summary>Saved links with placeholders, opened for an asset.</summary>
public interface IUrlActionService
{
    Task<Outcome<IReadOnlyList<UrlActionDto>>> ListAsync(CancellationToken ct = default);
    /// <summary>Creates (id null) or changes an action.</summary>
    Task<Outcome<UrlActionDto>> SaveAsync(User caller, int? id, UrlActionRequest req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>The action's link with the asset's values filled in. The caller must already have checked access to the asset.</summary>
    Task<Outcome<UrlActionRunDto>> RunAsync(User caller, int id, string agentId, CancellationToken ct = default);
}

/// <summary>Hardware check of Windows assets (serial, disk health, battery), a risk per asset, and warranty dates.</summary>
public interface IHardwareService
{
    Task<Outcome<HardwareDto>> GetAsync(string agentId, CancellationToken ct = default);
    /// <summary>Asks the agent now (a PowerShell script) and saves the answer.</summary>
    Task<Outcome<HardwareDto>> RefreshAsync(string agentId, CancellationToken ct = default);
    Task<Outcome<HardwareDto>> SetWarrantyAsync(User caller, string agentId, WarrantyRequest req, CancellationToken ct = default);
    /// <summary>Every asset the caller may see that has been checked or has a warranty date, worst first.</summary>
    Task<Outcome<IReadOnlyList<HardwareRowDto>>> ListAsync(User caller, CancellationToken ct = default);
}

/// <summary>Application catalog and rollouts: install a package (for example Acrobat Reader) on many assets, waiting for offline ones.</summary>
public interface IAppDeploymentService
{
    Task<Outcome<IReadOnlyList<SoftwarePackageDto>>> ListPackagesAsync(CancellationToken ct = default);
    Task<Outcome<SoftwarePackageDto>> CreatePackageAsync(User caller, SoftwarePackageRequest req, CancellationToken ct = default);
    Task<Outcome<SoftwarePackageDto>> UpdatePackageAsync(User caller, int id, SoftwarePackageRequest req, CancellationToken ct = default);
    Task<Outcome<bool>> DeletePackageAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<AppDeploymentDto>>> ListAsync(User caller, CancellationToken ct = default);
    Task<Outcome<AppDeploymentDto>> GetAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<AppDeploymentDto>> CreateAsync(User caller, AppDeploymentRequest req, CancellationToken ct = default);
    Task<Outcome<AppDeploymentDto>> RetryFailedAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>For the background runner: expires old waits, claims up to max waiting targets whose asset is online and returns what to run.</summary>
    Task<IReadOnlyList<AppWork>> ClaimDueAsync(int max, CancellationToken ct = default);
    /// <summary>For the background runner: records what the asset answered.</summary>
    Task FinishAsync(AppWork work, string? payload, string? error, CancellationToken ct = default);
}

/// <summary>Groups of assets. A policy on a group applies to every member.</summary>
public interface IAssetGroupService
{
    Task<Outcome<IReadOnlyList<AssetGroupDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<AssetGroupDetailDto>> GetAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<AssetGroupDetailDto>> CreateAsync(User caller, AssetGroupUpsert req, CancellationToken ct = default);
    Task<Outcome<AssetGroupDetailDto>> UpdateAsync(User caller, int id, AssetGroupUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<AssetGroupDetailDto>> AddMembersAsync(User caller, int id, GroupMembersRequest req, CancellationToken ct = default);
    Task<Outcome<AssetGroupDetailDto>> RemoveMembersAsync(User caller, int id, GroupMembersRequest req, CancellationToken ct = default);
}
