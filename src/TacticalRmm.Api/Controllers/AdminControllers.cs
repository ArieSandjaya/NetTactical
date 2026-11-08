using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TacticalRmm.Api.Auth;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
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
