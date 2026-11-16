using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TacticalRmm.Api.Auth;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Api.Controllers;

/// <summary>Port apiv3/views.py — endpoints called by the Go agent. Paths and JSON are identical to Django.</summary>
[ApiController, Route("api/v3")]
public class AgentV3Controller(
    IAgentService agents, ICheckService checks, ITaskService tasks,
    IWinUpdateService winUpdates, IAgentTelemetryService telemetry,
    IOptions<TacticalOptions> opt) : ControllerBase
{
    private async Task<Agent?> Me(CancellationToken ct) => await agents.GetByUserAsync(HttpContext.GetUser().Id, ct);

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPost("checkin")]
    public async Task<IActionResult> CheckIn(CancellationToken ct) => await Me(ct) is null ? NotFound() : Ok("ok");

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpGet("{agentId}/config")]
    public async Task<IActionResult> Config(string agentId, CancellationToken ct)
    {
        var me = await Me(ct);
        return me is null || me.AgentId != agentId ? NotFound() : Ok(AgentConfigFactory.Create(opt.Value.Agent));
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpGet("{agentId}/checkrunner")]
    public async Task<IActionResult> CheckRunner(string agentId, CancellationToken ct)
    {
        var me = await Me(ct);
        return me is null ? NotFound() : Ok(await checks.GetRunnerAsync(me, dueOnly: true, ct));
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpGet("{agentId}/runchecks")]
    public async Task<IActionResult> RunChecks(string agentId, CancellationToken ct)
    {
        var me = await Me(ct);
        return me is null ? NotFound() : Ok(await checks.GetRunnerAsync(me, dueOnly: false, ct));
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpGet("{agentId}/checkinterval")]
    public async Task<IActionResult> CheckInterval(string agentId, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        var r = await checks.GetRunnerAsync(me, dueOnly: false, ct);
        return Ok(new { agent = r.Agent, check_interval = r.CheckInterval });
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPatch("checkrunner")]
    public async Task<IActionResult> CheckResult(CheckResultPayload body, CancellationToken ct)
    {
        if (body.AgentId is null) return BadRequest("Agent upgrade required");
        var me = await Me(ct);
        if (me is null) return NotFound();
        var r = await checks.HandleResultAsync(me, body, ct);
        return r == "ok" ? Ok("ok") : Forbid();
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpGet("{pk:int}/{agentId}/taskrunner")]
    public async Task<IActionResult> TaskGet(int pk, string agentId, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        var t = await tasks.GetForAgentAsync(me, pk, ct);
        return t is null ? BadRequest("") : Ok(t);
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPatch("{pk:int}/{agentId}/taskrunner")]
    public async Task<IActionResult> TaskPatch(int pk, string agentId, TaskResultPayload body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        if (await tasks.GetForAgentAsync(me, pk, ct) is null) return Forbid(); // the task must apply to this agent
        await tasks.SaveResultAsync(me, pk, body, ct);
        return Ok("ok");
    }

    [Authorize(AuthDefaults.InstallerPolicy)]
    [HttpPost("newagent")]
    public async Task<IActionResult> NewAgent(NewAgentRequest body, CancellationToken ct)
    {
        try { return Ok(await agents.RegisterAsync(body, HttpContext.GetUser().Username, HttpContext.ClientIp(), ct)); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
    }

    // ------------------------------------------------------------------ simple telemetry

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPost("syncmesh")]
    public async Task<IActionResult> SyncMesh(SyncMeshPayload body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        if (string.IsNullOrWhiteSpace(body.NodeId)) return BadRequest("nodeid required");
        await telemetry.SetMeshNodeIdAsync(me, body.NodeId, ct);
        // body.RunSyncTask (sync_mesh_perms_task) not ported yet: Mesh permission sync is outside the core scope.
        return Ok("ok");
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPost("choco")]
    public async Task<IActionResult> Choco(ChocoPayload body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        await telemetry.SetChocoInstalledAsync(me, body.Installed, ct);
        return Ok("ok");
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPost("software")]
    public async Task<IActionResult> Software([FromBody] JsonElement body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        if (!body.TryGetProperty("software", out var sw)) return BadRequest("software required");
        await telemetry.SaveSoftwareAsync(me, sw.GetRawText(), ct);
        return Ok("ok");
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPatch("{pk:long}/{agentId}/histresult")]
    public async Task<IActionResult> HistResult(long pk, string agentId, [FromBody] JsonElement body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        if (!body.TryGetProperty("script_results", out var sr) || sr.ValueKind != JsonValueKind.Object)
            return BadRequest("script_results required");

        var node = JsonNode.Parse(sr.GetRawText())!.AsObject();
        if (Request.ContentLength > opt.Value.MaxRequestSizeBytes) // same as TRMM_MAX_REQUEST_SIZE in Django
        {
            node["stdout"] = "";
            node["stderr"] = "Content truncated due to excessive request size.";
            node["retcode"] = 1;
        }
        var stdout = node["stdout"]?.ToString();
        var ok = await telemetry.SaveHistoryResultAsync(me, pk, node.ToJsonString(), stdout, HttpContext.GetUser().Id, ct);
        // Catatan: the "collector" result (custom field) is not ported yet because CustomField is outside the core scope.
        return ok ? Ok("ok") : NotFound();
    }

    // ------------------------------------------------------------------ windows update

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPut("winupdates")]
    public async Task<IActionResult> PutWinUpdates(WinUpdatesPut body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        await winUpdates.SetRebootStateAsync(me, body.NeedsReboot, ct);
        return Ok("ok");
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPatch("winupdates")]
    public async Task<IActionResult> PatchWinUpdates(WinUpdatesPatch body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        return await winUpdates.MarkResultAsync(me, body.Guid, body.Success, ct) ? Ok("ok") : NotFound();
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPost("winupdates")]
    public async Task<IActionResult> PostWinUpdates(WinUpdatesPost body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        if (body.WuaUpdates is not { Count: > 0 }) return BadRequest("Empty payload");
        return await winUpdates.SyncAsync(me, body.WuaUpdates, ct) ? Ok("ok") : BadRequest("Empty payload");
    }

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpPost("superseded")]
    public async Task<IActionResult> Superseded(GuidPayload body, CancellationToken ct)
    {
        var me = await Me(ct);
        if (me is null) return NotFound();
        await winUpdates.RemoveByGuidAsync(me, body.Guid, ct);
        return Ok("ok");
    }

    // ------------------------------------------------------------------ installer & mesh

    [Authorize(AuthDefaults.InstallerPolicy)]
    [HttpGet("installer")]
    public IActionResult InstallerCheck() => Ok("ok"); // only validates the token (401 if invalid)

    [Authorize(AuthDefaults.InstallerPolicy)]
    [HttpPost("installer")]
    public IActionResult InstallerVersion(InstallerVersionPayload body)
    {
        if (string.IsNullOrWhiteSpace(body.Version)) return BadRequest("Invalid data");
        var latest = opt.Value.LatestAgentVer;
        return AgentVersion.IsInstallerOutdated(body.Version, latest)
            ? BadRequest($"Old installer detected (version {body.Version} ). Latest version is {latest} Please generate a new installer from the RMM")
            : Ok("ok");
    }

    // MeshCentral is not used by this server: agents are installed with --nomesh. These two routes only exist so an agent that still asks
    // for the MeshAgent gets a clear message instead of a 404.
    private const string NoMesh = "MeshCentral is not used by this server. Install the agent with --nomesh.";

    [Authorize(AuthDefaults.InstallerPolicy)]
    [HttpPost("meshexe")]
    public IActionResult MeshExe(MeshExePayload body) => BadRequest(NoMesh);

    [Authorize(AuthDefaults.AgentPolicy)]
    [HttpGet("{agentId}/meshreinstall")]
    public IActionResult MeshReinstall(string agentId) => BadRequest(NoMesh);
}
