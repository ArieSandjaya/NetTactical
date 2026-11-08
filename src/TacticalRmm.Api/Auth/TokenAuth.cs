using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Api.Auth;

public static class AuthDefaults
{
    public const string Scheme = "TrmmToken";
    public const string DashboardPolicy = "Dashboard";
    public const string AgentPolicy = "Agent";
    public const string InstallerPolicy = "Installer";
    public const string UserItem = "trmm.user";
}

public static class HttpContextExtensions
{
    public static User GetUser(this HttpContext c) => (User)c.Items[AuthDefaults.UserItem]!;
    public static string? ClientIp(this HttpContext c) => c.Connection.RemoteIpAddress?.ToString();
}

/// <summary>
/// Header "Authorization: Token xxx" (Knox / DRF authtoken) or "X-API-KEY: xxx" — same as Django.
/// </summary>
public sealed class TokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    ITokenService tokens, AppDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        User? user = null;

        if (Request.Headers.TryGetValue("X-API-KEY", out var apiKey) && !string.IsNullOrEmpty(apiKey))
        {
            var key = await db.ApiKeys.AsNoTracking().Include(k => k.User).ThenInclude(u => u.Role).ThenInclude(r => r!.CanViewClients)
                .Include(k => k.User).ThenInclude(u => u.Role).ThenInclude(r => r!.CanViewSites)
                .FirstOrDefaultAsync(k => k.Key == apiKey.ToString());
            if (key is null || !key.User.IsActive || (key.Expiration is { } e && e < DateTime.UtcNow))
                return AuthenticateResult.Fail("Invalid token.");
            user = key.User;
        }
        else if (Request.Headers.Authorization.ToString() is { Length: > 0 } h && h.StartsWith("Token ", StringComparison.OrdinalIgnoreCase))
        {
            user = await tokens.ValidateAsync(h["Token ".Length..].Trim(), Context.RequestAborted);
            if (user is null) return AuthenticateResult.Fail("Invalid token.");
        }
        else return AuthenticateResult.NoResult();

        if (user.AgentId is null && user.BlockDashboardLogin) return AuthenticateResult.Fail("Dashboard login blocked.");

        Context.Items[AuthDefaults.UserItem] = user;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("is_agent", (user.AgentId is not null).ToString().ToLowerInvariant()),
            new("is_installer", user.IsInstallerUser.ToString().ToLowerInvariant()),
        };
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, AuthDefaults.Scheme)), AuthDefaults.Scheme));
    }
}

/// <summary>[RequirePerm(Perm.X)] — agent/installer accounts are always rejected; an empty role is rejected.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermAttribute(string permission) : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        if (ctx.HttpContext.Items[AuthDefaults.UserItem] is not User user) { ctx.Result = new UnauthorizedResult(); return Task.CompletedTask; }
        var perms = ctx.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        if (!perms.HasPermission(user, permission)) ctx.Result = new ForbidResult();
        return Task.CompletedTask;
    }
}

public static class AuthExtensions
{
    public static IServiceCollection AddTrmmAuth(this IServiceCollection s)
    {
        s.AddAuthentication(AuthDefaults.Scheme).AddScheme<AuthenticationSchemeOptions, TokenAuthHandler>(AuthDefaults.Scheme, null);
        s.AddAuthorization(o =>
        {
            o.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            o.AddPolicy(AuthDefaults.DashboardPolicy, p => p.RequireAuthenticatedUser().RequireClaim("is_agent", "false").RequireClaim("is_installer", "false"));
            o.AddPolicy(AuthDefaults.AgentPolicy, p => p.RequireAuthenticatedUser().RequireClaim("is_agent", "true"));
            o.AddPolicy(AuthDefaults.InstallerPolicy, p => p.RequireAuthenticatedUser().RequireClaim("is_installer", "true"));
        });
        return s;
    }
}
