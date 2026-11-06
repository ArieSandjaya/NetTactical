using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TacticalRmm.Api.Auth;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Api.Controllers;

[ApiController]
public class AuthController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("v2/login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        var r = await auth.LoginAsync(req, HttpContext.ClientIp(), ct);
        if (r.TotpRequired) return Ok(new { totp = true });
        return r.Token is null ? BadRequest(new { error = r.Error }) : Ok(new { token = r.Token, expiry = r.Expiry });
    }

    [Authorize(AuthDefaults.DashboardPolicy)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var h = Request.Headers.Authorization.ToString();
        if (h.StartsWith("Token ", StringComparison.OrdinalIgnoreCase)) await auth.LogoutAsync(h[6..].Trim(), ct);
        return Ok();
    }
}
