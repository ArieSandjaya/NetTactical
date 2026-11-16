using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TacticalRmm.Api.Auth;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure;
using TacticalRmm.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTrmmAuth();
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower; // the Vue frontend uses snake_case
    o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
});
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024 * 1024); // = max_payload NATS

// Throttle login: 10/minute and 300/day per IP (TRMM_LOGIN_MIN_THROTTLE / DAY_THROTTLE)
static RateLimitPartition<string> LoginLimit(HttpContext ctx, string suffix, int permits, TimeSpan window) =>
    ctx.Request.Path.StartsWithSegments("/v2/login")
        ? RateLimitPartition.GetFixedWindowLimiter($"{ctx.Connection.RemoteIpAddress}-{suffix}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window })
        : RateLimitPartition.GetNoLimiter("none");

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(c => LoginLimit(c, "min", 10, TimeSpan.FromMinutes(1))),
        PartitionedRateLimiter.Create<HttpContext, string>(c => LoginLimit(c, "day", 300, TimeSpan.FromDays(1))));
});

var origins = builder.Configuration.GetSection("Tactical:CorsOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins).AllowAnyMethod().WithHeaders("authorization", "content-type", "x-api-key", "content-range")
     .WithExposedHeaders("content-range")));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
    await Bootstrap.RunAsync(scope.ServiceProvider, app.Configuration);

// Static dashboard UI (wwwroot). Strict CSP: same-origin sources only, no inline scripts/styles.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;

/// <summary>Creates the schema (dev) and the initial account when the database is empty.</summary>
static class Bootstrap
{
    public static async Task RunAsync(IServiceProvider sp, IConfiguration cfg)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        // Production: replace with `dotnet ef migrations add Initial` then db.Database.MigrateAsync().
        await db.Database.EnsureCreatedAsync();
        await SchemaUpgrade.RunAsync(db, sp.GetRequiredService<ILogger<Program>>());   // databases created by an earlier version get the newer tables/columns

        if (await db.Users.AnyAsync()) return;
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var tokens = sp.GetRequiredService<ITokenService>();

        var adminUser = cfg["Tactical:BootstrapAdmin:Username"] ?? "admin";
        var adminPass = cfg["Tactical:BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(adminPass) || adminPass.Length < 12)
            throw new InvalidOperationException("Set Tactical:BootstrapAdmin:Password (min 12 characters) to create the first admin.");

        db.Users.Add(new User { Username = adminUser, PasswordHash = hasher.Hash(adminPass), IsSuperuser = true });
        var installer = new User { Username = "installer-user", PasswordHash = hasher.Hash(Guid.NewGuid().ToString("N")), IsInstallerUser = true };
        db.Users.Add(installer);
        await db.SaveChangesAsync();

        // The installer token is used in the agent install command; shown once in the log.
        var tok = await tokens.CreateAgentTokenAsync(installer.Id);
        sp.GetRequiredService<ILogger<Program>>().LogWarning("Installer token (save it, shown only once): {Token}", tok);
    }
}
