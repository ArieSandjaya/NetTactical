using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TacticalRmm.Api.Auth;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;

namespace TacticalRmm.Api.Controllers;

internal static class OutcomeExtensions
{
    /// <summary>Map Outcome to an HTTP response: 200 / 404 / 403 / 400 / 409.</summary>
    public static IActionResult ToResult<T>(this ControllerBase c, Outcome<T> o) => o.Status switch
    {
        OutcomeStatus.Ok => c.Ok(o.Value),
        OutcomeStatus.NotFound => c.NotFound(new { detail = o.Error }),
        OutcomeStatus.Forbidden => new ObjectResult(new { detail = o.Error }) { StatusCode = StatusCodes.Status403Forbidden },
        OutcomeStatus.Conflict => c.Conflict(new { detail = o.Error }),
        _ => c.BadRequest(new { detail = o.Error }),
    };
}

// ------------------------------------------------------------------------------------------ checks
[ApiController, Authorize(AuthDefaults.DashboardPolicy)]
public class ChecksController(ICheckAdminService svc) : ControllerBase
{
    [HttpGet("agents/{agentId}/checks"), RequirePerm(Perm.ListChecks)]
    public async Task<IActionResult> ForAgent(string agentId, CancellationToken ct) => this.ToResult(await svc.ListForAgentAsync(HttpContext.GetUser(), agentId, ct));

    [HttpGet("automation/policies/{policyId:int}/checks"), RequirePerm(Perm.ListChecks)]
    public async Task<IActionResult> ForPolicy(int policyId, CancellationToken ct) => this.ToResult(await svc.ListForPolicyAsync(HttpContext.GetUser(), policyId, ct));

    [HttpGet("checks/{id:int}"), RequirePerm(Perm.ListChecks)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => this.ToResult(await svc.GetAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("checks"), RequirePerm(Perm.ManageChecks)]
    public async Task<IActionResult> Create(CheckUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("checks/{id:int}"), RequirePerm(Perm.ManageChecks)]
    public async Task<IActionResult> Update(int id, CheckUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("checks/{id:int}"), RequirePerm(Perm.ManageChecks)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));
}

// ------------------------------------------------------------------------------------------ tasks
public class RunTaskRequest { public string? AgentId { get; set; } }

[ApiController, Authorize(AuthDefaults.DashboardPolicy)]
public class TasksController(ITaskAdminService svc) : ControllerBase
{
    [HttpGet("agents/{agentId}/tasks"), RequirePerm(Perm.ListTasks)]
    public async Task<IActionResult> ForAgent(string agentId, CancellationToken ct) => this.ToResult(await svc.ListForAgentAsync(HttpContext.GetUser(), agentId, ct));

    [HttpGet("automation/policies/{policyId:int}/tasks"), RequirePerm(Perm.ListTasks)]
    public async Task<IActionResult> ForPolicy(int policyId, CancellationToken ct) => this.ToResult(await svc.ListForPolicyAsync(HttpContext.GetUser(), policyId, ct));

    [HttpGet("tasks/{id:int}"), RequirePerm(Perm.ListTasks)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => this.ToResult(await svc.GetAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("tasks"), RequirePerm(Perm.ManageTasks)]
    public async Task<IActionResult> Create(TaskUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("tasks/{id:int}"), RequirePerm(Perm.ManageTasks)]
    public async Task<IActionResult> Update(int id, TaskUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("tasks/{id:int}"), RequirePerm(Perm.ManageTasks)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("tasks/{id:int}/run"), RequirePerm(Perm.RunTasks)]
    public async Task<IActionResult> Run(int id, [FromBody] RunTaskRequest? req, CancellationToken ct) => this.ToResult(await svc.RunNowAsync(HttpContext.GetUser(), id, req?.AgentId, ct));
}

// ------------------------------------------------------------------------------------------ accounts
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("accounts")]
public class AccountsController(IUserAdminService users, IRoleAdminService roles) : ControllerBase
{
    [HttpGet("users"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> ListUsers(CancellationToken ct) => this.ToResult(await users.ListAsync(HttpContext.GetUser(), ct));

    [HttpGet("users/{id:int}"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> GetUser(int id, CancellationToken ct) => this.ToResult(await users.GetAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("users"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> CreateUser(UserCreate req, CancellationToken ct) => this.ToResult(await users.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("users/{id:int}"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> UpdateUser(int id, UserUpdate req, CancellationToken ct) => this.ToResult(await users.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("users/{id:int}"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> DeleteUser(int id, CancellationToken ct) => this.ToResult(await users.DeleteAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("users/{id:int}/reset-password"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req, CancellationToken ct) =>
        this.ToResult(await users.ResetPasswordAsync(HttpContext.GetUser(), id, req.Password, ct));

    [HttpPost("users/{id:int}/reset-totp"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> ResetTotp(int id, CancellationToken ct) => this.ToResult(await users.ResetTotpAsync(HttpContext.GetUser(), id, ct));

    // Own 2FA: no special permission required
    [HttpPost("totp/setup")]
    public async Task<IActionResult> TotpSetup(CancellationToken ct) => this.ToResult(await users.BeginTotpAsync(HttpContext.GetUser(), ct));

    [HttpGet("totp/status")]
    public IActionResult TotpStatus() => Ok(new TotpStatus(!string.IsNullOrEmpty(HttpContext.GetUser().TotpKey)));

    [HttpPost("totp/disable")]
    public async Task<IActionResult> TotpDisable(TotpDisableRequest req, CancellationToken ct) =>
        this.ToResult(await users.DisableTotpAsync(HttpContext.GetUser(), req.Password, req.Code, ct));

    [HttpPost("totp/enable")]
    public async Task<IActionResult> TotpEnable(TotpEnableRequest req, CancellationToken ct) =>
        this.ToResult(await users.EnableTotpAsync(HttpContext.GetUser(), req.Secret, req.Code, ct));

    [HttpGet("permissions"), RequirePerm(Perm.ManageAccounts)]
    public IActionResult Permissions() => Ok(roles.AllPermissions);

    [HttpGet("roles"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> ListRoles(CancellationToken ct) => this.ToResult(await roles.ListAsync(HttpContext.GetUser(), ct));

    [HttpPost("roles"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> CreateRole(RoleUpsert req, CancellationToken ct) => this.ToResult(await roles.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("roles/{id:int}"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> UpdateRole(int id, RoleUpsert req, CancellationToken ct) => this.ToResult(await roles.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("roles/{id:int}"), RequirePerm(Perm.ManageAccounts)]
    public async Task<IActionResult> DeleteRole(int id, CancellationToken ct) => this.ToResult(await roles.DeleteAsync(HttpContext.GetUser(), id, ct));
}

// ------------------------------------------------------------------------------------------ organisation
[ApiController, Authorize(AuthDefaults.DashboardPolicy)]
public class OrgController(IOrgAdminService org) : ControllerBase
{
    [HttpPut("clients/{id:int}"), RequirePerm(Perm.ManageClients)]
    public async Task<IActionResult> UpdateClient(int id, ClientUpdate req, CancellationToken ct) => this.ToResult(await org.UpdateClientAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("clients/{id:int}"), RequirePerm(Perm.ManageClients)]
    public async Task<IActionResult> DeleteClient(int id, CancellationToken ct) => this.ToResult(await org.DeleteClientAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("clients/sites"), RequirePerm(Perm.ManageSites)]
    public async Task<IActionResult> AddSite(SiteUpsert req, CancellationToken ct) => this.ToResult(await org.AddSiteAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("sites/{id:int}"), RequirePerm(Perm.ManageSites)]
    public async Task<IActionResult> UpdateSite(int id, SiteUpsert req, CancellationToken ct) => this.ToResult(await org.UpdateSiteAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("sites/{id:int}"), RequirePerm(Perm.ManageSites)]
    public async Task<IActionResult> DeleteSite(int id, CancellationToken ct) => this.ToResult(await org.DeleteSiteAsync(HttpContext.GetUser(), id, ct));

    [HttpPut("agents/{agentId}"), RequirePerm(Perm.EditAgent)]
    public async Task<IActionResult> UpdateAgent(string agentId, AgentUpdate req, CancellationToken ct) => this.ToResult(await org.UpdateAgentAsync(HttpContext.GetUser(), agentId, req, ct));
}

// ------------------------------------------------------------------------------------------ policy
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("automation/policies")]
public class PoliciesController(IPolicyAdminService svc) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListPolicies)]
    public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(ct));

    [HttpPost, RequirePerm(Perm.ManagePolicies)]
    public async Task<IActionResult> Create(PolicyUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("{id:int}"), RequirePerm(Perm.ManagePolicies)]
    public async Task<IActionResult> Update(int id, PolicyUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("{id:int}"), RequirePerm(Perm.ManagePolicies)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));
}

// ------------------------------------------------------------------------------------------ alert templates
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("alert-templates")]
public class AlertTemplatesController(IAlertTemplateAdminService svc) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListAlerts)]
    public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(ct));

    [HttpPost, RequirePerm(Perm.ManageAlerts)]
    public async Task<IActionResult> Create(AlertTemplateUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("{id:int}"), RequirePerm(Perm.ManageAlerts)]
    public async Task<IActionResult> Update(int id, AlertTemplateUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("{id:int}"), RequirePerm(Perm.ManageAlerts)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));
}

// ------------------------------------------------------------------------------------------ custom fields
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("custom-fields")]
public class CustomFieldsController(ICustomFieldService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? model, CancellationToken ct) => this.ToResult(await svc.ListAsync(model, ct));

    [HttpPost, RequirePerm(Perm.ManageCustomFields)]
    public async Task<IActionResult> Create(CustomFieldUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("{id:int}"), RequirePerm(Perm.ManageCustomFields)]
    public async Task<IActionResult> Update(int id, CustomFieldUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("{id:int}"), RequirePerm(Perm.ManageCustomFields)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));

    // Reading and editing values is checked inside the service: the permission depends on the model, and the caller's client/site scope applies.
    [HttpGet("values/{model}/{target}")]
    public async Task<IActionResult> Values(string model, string target, CancellationToken ct) => this.ToResult(await svc.ValuesAsync(HttpContext.GetUser(), model, target, ct));

    [HttpPut("values/{model}/{target}")]
    public async Task<IActionResult> SetValues(string model, string target, CustomFieldValuesSet req, CancellationToken ct) => this.ToResult(await svc.SetValuesAsync(HttpContext.GetUser(), model, target, req, ct));
}

// ------------------------------------------------------------------------------------------ script snippets
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("script-snippets")]
public class ScriptSnippetsController(ISnippetService svc) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListScripts)]
    public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(ct));

    [HttpGet("{id:int}"), RequirePerm(Perm.ListScripts)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => this.ToResult(await svc.GetAsync(id, ct));

    [HttpPost, RequirePerm(Perm.ManageScripts)]
    public async Task<IActionResult> Create(SnippetUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("{id:int}"), RequirePerm(Perm.ManageScripts)]
    public async Task<IActionResult> Update(int id, SnippetUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("{id:int}"), RequirePerm(Perm.ManageScripts)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));
}

// ------------------------------------------------------------------------------------------ installed software
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("agents/{agentId}/software")]
public class SoftwareController(ISoftwareService svc, IPermissionService perms) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> Get(string agentId, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        return this.ToResult(await svc.GetAsync(agentId, ct));
    }

    /// <summary>Asks the agent for its list now (the agent must be online).</summary>
    [HttpPost("refresh"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> Refresh(string agentId, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        return this.ToResult(await svc.RefreshAsync(agentId, HttpContext.GetUser().Username, ct));
    }

    /// <summary>Removes one application from the stored list by running its uninstall command on the asset. Same permission as running a command.</summary>
    [HttpPost("uninstall"), RequirePerm(Perm.ManageSoftware)]
    public async Task<IActionResult> Uninstall(string agentId, SoftwareUninstallRequest req, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        return this.ToResult(await svc.UninstallAsync(agentId, req, HttpContext.GetUser().Username, ct));
    }
}

// ------------------------------------------------------------------------------------------ reports
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("reports")]
public class ReportsController(IReportService svc) : ControllerBase
{
    // Which reports a caller can run depends on their permissions, so the checks are inside the service.
    [HttpGet]
    public IActionResult List() => Ok(svc.Available(HttpContext.GetUser()));

    [HttpGet("{name}")]
    public async Task<IActionResult> Run(string name, [FromQuery(Name = "client_id")] int? clientId, [FromQuery(Name = "site_id")] int? siteId,
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? q, CancellationToken ct) =>
        this.ToResult(await svc.BuildAsync(HttpContext.GetUser(), name, clientId, siteId, from, to, q, maxRows: 200, ct: ct));

    [HttpGet("{name}/csv")]
    public async Task<IActionResult> Csv(string name, [FromQuery(Name = "client_id")] int? clientId, [FromQuery(Name = "site_id")] int? siteId,
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? q, CancellationToken ct)
    {
        var r = await svc.CsvAsync(HttpContext.GetUser(), name, clientId, siteId, from, to, q, ct);
        return r.IsOk ? File(r.Value!.Content, "text/csv; charset=utf-8", r.Value.FileName) : this.ToResult(r);
    }
}

// ------------------------------------------------------------------------------------------ VNC
[ApiController, Authorize(AuthDefaults.DashboardPolicy)]
public class VncController(IVncService svc) : ControllerBase
{
    [HttpGet("vnc/credentials"), RequirePerm(Perm.ManageVnc)]
    public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(ct));

    [HttpPost("vnc/credentials"), RequirePerm(Perm.ManageVnc)]
    public async Task<IActionResult> Create(VncCredentialUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("vnc/credentials/{id:int}"), RequirePerm(Perm.ManageVnc)]
    public async Task<IActionResult> Update(int id, VncCredentialUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("vnc/credentials/{id:int}"), RequirePerm(Perm.ManageVnc)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("agents/{agentId}/vnc/test"), RequirePerm(Perm.UseVnc)]
    public async Task<IActionResult> Test(string agentId, CancellationToken ct) => this.ToResult(await svc.TestAsync(HttpContext.GetUser(), agentId, ct));

    /// <summary>Returns a .vnc file for TightVNC Viewer. It contains the working password (obfuscated), so it is never cached.</summary>
    [HttpPost("agents/{agentId}/vnc/file"), RequirePerm(Perm.UseVnc)]
    public async Task<IActionResult> File(string agentId, CancellationToken ct)
    {
        var r = await svc.PrepareFileAsync(HttpContext.GetUser(), agentId, ct);
        if (r.Status != OutcomeStatus.Ok) return this.ToResult(r);
        Response.Headers.CacheControl = "no-store";
        return base.File(System.Text.Encoding.ASCII.GetBytes(r.Value!.Content), "application/octet-stream", r.Value.FileName);
    }
}

// ------------------------------------------------------------------------------------------ Agent installer
[ApiController, Authorize(AuthDefaults.DashboardPolicy)]
public class InstallerController(IInstallerService svc) : ControllerBase
{
    /// <summary>Returns a one-time token and ready-to-run install commands. The response is never cached because it contains the token.</summary>
    [HttpPost("installer/generate"), RequirePerm(Perm.InstallAgents)]
    public async Task<IActionResult> Generate(InstallerRequest req, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return this.ToResult(await svc.GenerateAsync(HttpContext.GetUser(), req, $"{Request.Scheme}://{Request.Host}", ct));
    }
}

/// <summary>Deployment links: create, list and delete (dashboard), and the public script a link serves.</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("deployments")]
public class DeploymentsController(IDeploymentService svc) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListDeployments)]
    public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(HttpContext.GetUser(), ct));

    [HttpPost, RequirePerm(Perm.ManageDeployments)]
    public async Task<IActionResult> Create(DeploymentRequest req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, $"{Request.Scheme}://{Request.Host}", ct));

    [HttpDelete("{id:int}"), RequirePerm(Perm.ManageDeployments)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));
}

/// <summary>Asset groups. Listing needs the asset list permission; changes need the group permission.</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("asset-groups")]
public class AssetGroupsController(IAssetGroupService svc) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ListAgents)] public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(ct));
    [HttpGet("{id:int}"), RequirePerm(Perm.ListAgents)] public async Task<IActionResult> Get(int id, CancellationToken ct) => this.ToResult(await svc.GetAsync(HttpContext.GetUser(), id, ct));
    [HttpPost, RequirePerm(Perm.ManageGroups)] public async Task<IActionResult> Create(AssetGroupUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));
    [HttpPut("{id:int}"), RequirePerm(Perm.ManageGroups)] public async Task<IActionResult> Update(int id, AssetGroupUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));
    [HttpDelete("{id:int}"), RequirePerm(Perm.ManageGroups)] public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));
    [HttpPost("{id:int}/members"), RequirePerm(Perm.ManageGroups)] public async Task<IActionResult> Add(int id, GroupMembersRequest req, CancellationToken ct) => this.ToResult(await svc.AddMembersAsync(HttpContext.GetUser(), id, req, ct));
    [HttpPost("{id:int}/members/remove"), RequirePerm(Perm.ManageGroups)] public async Task<IActionResult> Remove(int id, GroupMembersRequest req, CancellationToken ct) => this.ToResult(await svc.RemoveMembersAsync(HttpContext.GetUser(), id, req, ct));
}

/// <summary>Application catalog (what can be installed) and rollouts of it to assets.</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("software-packages"), RequirePerm(Perm.ManageSoftware)]
public class SoftwarePackagesController(IAppDeploymentService svc) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListPackagesAsync(ct));
    [HttpPost] public async Task<IActionResult> Create(SoftwarePackageRequest req, CancellationToken ct) => this.ToResult(await svc.CreatePackageAsync(HttpContext.GetUser(), req, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, SoftwarePackageRequest req, CancellationToken ct) => this.ToResult(await svc.UpdatePackageAsync(HttpContext.GetUser(), id, req, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeletePackageAsync(HttpContext.GetUser(), id, ct));
}

[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("app-deployments"), RequirePerm(Perm.ManageSoftware)]
public class AppDeploymentsController(IAppDeploymentService svc) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(HttpContext.GetUser(), ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => this.ToResult(await svc.GetAsync(HttpContext.GetUser(), id, ct));
    [HttpPost] public async Task<IActionResult> Create(AppDeploymentRequest req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));
    [HttpPost("{id:int}/retry")] public async Task<IActionResult> Retry(int id, CancellationToken ct) => this.ToResult(await svc.RetryFailedAsync(HttpContext.GetUser(), id, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));
}

/// <summary>Public (no login): the link itself is the secret. Never cached; unknown, expired and used-up links all answer 404 the same way.</summary>
[ApiController, AllowAnonymous, Route("deploy")]
public class DeployLinkController(IDeploymentService svc) : ControllerBase
{
    [HttpGet("{uid}")]
    public async Task<IActionResult> Script(string uid, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var r = await svc.ScriptAsync(uid, ct);
        return r.Status == OutcomeStatus.Ok ? Content(r.Value!, "text/plain; charset=utf-8") : NotFound("This link is not valid.");
    }
}

/// <summary>Push install: the server installs the agent on remote Windows computers with a domain administrator account (WinRM).</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("installer/push"), RequirePerm(Perm.PushInstall)]
public class PushInstallController(IPushInstallService svc) : ControllerBase
{
    /// <summary>The body carries the domain credential, so nothing is cached. Returns a job immediately; poll GET {id}.</summary>
    [HttpPost]
    public async Task<IActionResult> Start(PushInstallRequest req, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return this.ToResult(await svc.StartAsync(HttpContext.GetUser(), req, $"{Request.Scheme}://{Request.Host}", ct));
    }

    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(HttpContext.GetUser(), ct));

    [HttpGet("{id}")] public async Task<IActionResult> Get(string id, CancellationToken ct) => this.ToResult(await svc.GetAsync(HttpContext.GetUser(), id, ct));
}

// ------------------------------------------------------------------------------------------ services and processes of an asset
/// <summary>A live look at the asset (the agent is asked each time). Everything here needs the permission to run commands, as it can change the computer.</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("agents/{agentId}")]
public class RemoteToolsController(IRemoteToolsService svc, IPermissionService perms) : ControllerBase
{
    private async Task<bool> MayAsync(string agentId, CancellationToken ct) => await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct);

    [HttpGet("services"), RequirePerm(Perm.ManageProcs)]
    public async Task<IActionResult> Services(string agentId, CancellationToken ct) { Response.Headers.CacheControl = "no-store"; return !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.ServicesAsync(agentId, ct)); }

    [HttpPost("services/action"), RequirePerm(Perm.ManageProcs)]
    public async Task<IActionResult> ServiceAction(string agentId, ServiceActionRequest req, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.ServiceActionAsync(HttpContext.GetUser(), agentId, req, ct));

    [HttpGet("processes"), RequirePerm(Perm.ManageProcs)]
    public async Task<IActionResult> Processes(string agentId, CancellationToken ct) { Response.Headers.CacheControl = "no-store"; return !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.ProcessesAsync(agentId, ct)); }

    [HttpPost("processes/kill"), RequirePerm(Perm.ManageProcs)]
    public async Task<IActionResult> Kill(string agentId, KillRequest req, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.KillAsync(HttpContext.GetUser(), agentId, req, ct));
}

// ------------------------------------------------------------------------------------------ Windows event log of an asset
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("agents/{agentId}/eventlog")]
public class EventLogController(IEventLogService svc, IPermissionService perms) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ViewEventLogs)]
    public async Task<IActionResult> Read(string agentId, [FromQuery] string? log, [FromQuery] string? level, [FromQuery] int hours = 24, [FromQuery] int max = EventLogs.DefaultEntries,
        [FromQuery] string? text = null, [FromQuery(Name = "event_id")] int? eventId = null, CancellationToken ct = default)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return this.ToResult(await svc.ReadAsync(agentId, log ?? "System", level ?? "all", hours, max, text, eventId, ct));
    }
}

// ------------------------------------------------------------------------------------------ file browser of an asset
/// <summary>Browse, download, upload, create, rename and delete files on a Windows asset. As powerful as running commands, so it needs that permission.</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("agents/{agentId}/files"), RequestSizeLimit(16 * 1024 * 1024)]
public class FileBrowserController(IFileBrowserService svc, IPermissionService perms) : ControllerBase
{
    private async Task<bool> MayAsync(string agentId, CancellationToken ct) => await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct);

    [HttpGet, RequirePerm(Perm.RunCommands)]
    public async Task<IActionResult> List(string agentId, [FromQuery] string? path, CancellationToken ct) { Response.Headers.CacheControl = "no-store"; return !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.ListAsync(agentId, path, ct)); }

    [HttpGet("download"), RequirePerm(Perm.UseMesh)]
    public async Task<IActionResult> Download(string agentId, [FromQuery] string? path, CancellationToken ct)
    {
        if (!await MayAsync(agentId, ct)) return Forbid();
        var r = await svc.DownloadAsync(HttpContext.GetUser(), agentId, path, ct);
        Response.Headers.CacheControl = "no-store";
        return r.IsOk ? File(r.Value!.Content, "application/octet-stream", r.Value.FileName) : this.ToResult(r);
    }

    [HttpPost("upload"), RequirePerm(Perm.UseMesh)]
    public async Task<IActionResult> Upload(string agentId, FileUploadRequest req, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.UploadAsync(HttpContext.GetUser(), agentId, req, ct));

    [HttpPost("folder"), RequirePerm(Perm.UseMesh)]
    public async Task<IActionResult> NewFolder(string agentId, FolderRequest req, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.NewFolderAsync(HttpContext.GetUser(), agentId, req, ct));

    [HttpPost("delete"), RequirePerm(Perm.UseMesh)]
    public async Task<IActionResult> Delete(string agentId, FileDeleteRequest req, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), agentId, req, ct));

    [HttpPost("rename"), RequirePerm(Perm.UseMesh)]
    public async Task<IActionResult> Rename(string agentId, FileRenameRequest req, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.RenameAsync(HttpContext.GetUser(), agentId, req, ct));
}

// ------------------------------------------------------------------------------------------ notes and history of an asset
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("agents/{agentId}")]
public class AgentLogController(IAgentLogService svc, IPermissionService perms) : ControllerBase
{
    private async Task<bool> MayAsync(string agentId, CancellationToken ct) => await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct);

    [HttpGet("history"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> History(string agentId, [FromQuery] int? take, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.HistoryAsync(agentId, take, ct));

    [HttpGet("notes"), RequirePerm(Perm.ListNotes)]
    public async Task<IActionResult> Notes(string agentId, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.NotesAsync(agentId, ct));

    [HttpPost("notes"), RequirePerm(Perm.ManageNotes)]
    public async Task<IActionResult> AddNote(string agentId, NoteRequest req, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.AddNoteAsync(HttpContext.GetUser(), agentId, req.Note, ct));

    [HttpDelete("notes/{noteId:int}"), RequirePerm(Perm.ManageNotes)]
    public async Task<IActionResult> DeleteNote(string agentId, int noteId, CancellationToken ct) =>
        !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.DeleteNoteAsync(HttpContext.GetUser(), agentId, noteId, ct));
}

// ------------------------------------------------------------------------------------------ bulk actions
/// <summary>One action (command, script, restart) on several assets. The permission and the access to every asset are checked in the service.</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("bulk-actions")]
public class BulkController(IBulkService svc) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start(BulkRequest req, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return this.ToResult(await svc.StartAsync(HttpContext.GetUser(), req, ct));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return this.ToResult(await svc.GetAsync(HttpContext.GetUser(), id, ct));
    }
}

// ------------------------------------------------------------------------------------------ Windows Update
[ApiController, Authorize(AuthDefaults.DashboardPolicy)]
public class WinUpdateAdminController(IWinUpdateAdminService svc, IPermissionService perms) : ControllerBase
{
    private async Task<bool> Allowed(string agentId, CancellationToken ct) => await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct);

    [HttpGet("agents/{agentId}/winupdates"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> List(string agentId, CancellationToken ct) => !await Allowed(agentId, ct) ? Forbid() : this.ToResult(await svc.ListAsync(agentId, ct));

    [HttpPut("agents/{agentId}/winupdates/action"), RequirePerm(Perm.ManageWinUpdates)]
    public async Task<IActionResult> SetAction(string agentId, WinUpdateActionRequest req, CancellationToken ct) =>
        !await Allowed(agentId, ct) ? Forbid() : this.ToResult(await svc.SetActionAsync(HttpContext.GetUser(), agentId, req, ct));

    [HttpPost("agents/{agentId}/winupdates/scan"), RequirePerm(Perm.ManageWinUpdates)]
    public async Task<IActionResult> Scan(string agentId, CancellationToken ct) => !await Allowed(agentId, ct) ? Forbid() : this.ToResult(await svc.ScanAsync(HttpContext.GetUser(), agentId, ct));

    [HttpPost("agents/{agentId}/winupdates/install"), RequirePerm(Perm.ManageWinUpdates)]
    public async Task<IActionResult> Install(string agentId, CancellationToken ct) => !await Allowed(agentId, ct) ? Forbid() : this.ToResult(await svc.InstallNowAsync(HttpContext.GetUser(), agentId, ct));

    [HttpGet("agents/{agentId}/winupdates/policy"), RequirePerm(Perm.ListAgents)]
    public async Task<IActionResult> GetAgentPolicy(string agentId, CancellationToken ct) => !await Allowed(agentId, ct) ? Forbid() : this.ToResult(await svc.GetAgentPolicyAsync(agentId, ct));

    [HttpPut("agents/{agentId}/winupdates/policy"), RequirePerm(Perm.ManageWinUpdates)]
    public async Task<IActionResult> SaveAgentPolicy(string agentId, WinUpdatePolicyDto req, CancellationToken ct) =>
        !await Allowed(agentId, ct) ? Forbid() : this.ToResult(await svc.SaveAgentPolicyAsync(HttpContext.GetUser(), agentId, req, ct));

    [HttpGet("automation/policies/{id:int}/winupdates"), RequirePerm(Perm.ListPolicies)]
    public async Task<IActionResult> GetPolicy(int id, CancellationToken ct) => this.ToResult(await svc.GetPolicyAsync(id, ct));

    [HttpPut("automation/policies/{id:int}/winupdates"), RequirePerm(Perm.ManagePolicies)]
    public async Task<IActionResult> SavePolicy(int id, WinUpdatePolicyDto req, CancellationToken ct) => this.ToResult(await svc.SavePolicyAsync(HttpContext.GetUser(), id, req, ct));
}

// ------------------------------------------------------------------------------------------ Alert notifications
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("notifications/channels"), RequirePerm(Perm.ManageNotifications)]
public class NotificationsController(INotificationAdminService svc) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(NotificationChannelUpsert req, CancellationToken ct) => this.ToResult(await svc.CreateAsync(HttpContext.GetUser(), req, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, NotificationChannelUpsert req, CancellationToken ct) => this.ToResult(await svc.UpdateAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("{id:int}/test")]
    public async Task<IActionResult> Test(int id, CancellationToken ct) => this.ToResult(await svc.TestAsync(HttpContext.GetUser(), id, ct));
}

// ------------------------------------------------------------------------------------------ server maintenance
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("maintenance")]
public class ServerMaintenanceController(IServerMaintenanceService svc) : ControllerBase
{
    [HttpGet("preview"), RequirePerm(Perm.DoServerMaint)]
    public async Task<IActionResult> Preview([FromQuery] string? action, [FromQuery] int days, CancellationToken ct) { Response.Headers.CacheControl = "no-store"; return this.ToResult(await svc.PreviewAsync(action, days, ct)); }

    [HttpPost("run"), RequirePerm(Perm.DoServerMaint)]
    public async Task<IActionResult> Run(MaintenanceRequest req, CancellationToken ct) => this.ToResult(await svc.RunAsync(HttpContext.GetUser(), req.Action, req.Days, ct));
}

// ------------------------------------------------------------------------------------------ URL actions
/// <summary>Saved links with placeholders. Anyone who may run them can list them; changing them needs the core-settings permission.</summary>
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("urlactions")]
public class UrlActionsController(IUrlActionService svc, IPermissionService perms) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.RunUrlActions)]
    public async Task<IActionResult> List(CancellationToken ct) => this.ToResult(await svc.ListAsync(ct));

    [HttpPost, RequirePerm(Perm.EditCoreSettings)]
    public async Task<IActionResult> Create(UrlActionRequest req, CancellationToken ct) => this.ToResult(await svc.SaveAsync(HttpContext.GetUser(), null, req, ct));

    [HttpPut("{id:int}"), RequirePerm(Perm.EditCoreSettings)]
    public async Task<IActionResult> Update(int id, UrlActionRequest req, CancellationToken ct) => this.ToResult(await svc.SaveAsync(HttpContext.GetUser(), id, req, ct));

    [HttpDelete("{id:int}"), RequirePerm(Perm.EditCoreSettings)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => this.ToResult(await svc.DeleteAsync(HttpContext.GetUser(), id, ct));

    [HttpPost("{id:int}/run"), RequirePerm(Perm.RunUrlActions)]
    public async Task<IActionResult> Run(int id, UrlActionRunRequest req, CancellationToken ct)
    {
        if (!await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), req.AgentId, ct)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return this.ToResult(await svc.RunAsync(HttpContext.GetUser(), id, req.AgentId, ct));
    }
}

// ------------------------------------------------------------------------------------------ hardware check and warranty
[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("hardware")]
public class HardwareController(IHardwareService svc) : ControllerBase
{
    [HttpGet, RequirePerm(Perm.ViewHardware)]
    public async Task<IActionResult> List(CancellationToken ct) { Response.Headers.CacheControl = "no-store"; return this.ToResult(await svc.ListAsync(HttpContext.GetUser(), ct)); }
}

[ApiController, Authorize(AuthDefaults.DashboardPolicy), Route("agents/{agentId}/hardware")]
public class AgentHardwareController(IHardwareService svc, IPermissionService perms) : ControllerBase
{
    private async Task<bool> MayAsync(string agentId, CancellationToken ct) => await perms.HasPermissionOnAgentAsync(HttpContext.GetUser(), agentId, ct);

    [HttpGet, RequirePerm(Perm.ViewHardware)]
    public async Task<IActionResult> Get(string agentId, CancellationToken ct) { Response.Headers.CacheControl = "no-store"; return !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.GetAsync(agentId, ct)); }

    [HttpPost("refresh"), RequirePerm(Perm.ViewHardware)]
    public async Task<IActionResult> Refresh(string agentId, CancellationToken ct) => !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.RefreshAsync(agentId, ct));

    [HttpPut("warranty"), RequirePerm(Perm.EditAgent)]
    public async Task<IActionResult> Warranty(string agentId, WarrantyRequest req, CancellationToken ct) => !await MayAsync(agentId, ct) ? Forbid() : this.ToResult(await svc.SetWarrantyAsync(HttpContext.GetUser(), agentId, req, ct));
}
