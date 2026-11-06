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
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower; // frontend Vue memakai snake_case
    o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
});
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024 * 1024); // = max_payload NATS

// Throttle login: 10/menit dan 300/hari per IP (TRMM_LOGIN_MIN_THROTTLE / DAY_THROTTLE)
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

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => "Tactical RMM (.NET)");
app.Run();

public partial class Program;

/// <summary>Membuat skema (dev) dan akun awal bila database kosong.</summary>
static class Bootstrap
{
    public static async Task RunAsync(IServiceProvider sp, IConfiguration cfg)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        // Produksi: ganti dengan `dotnet ef migrations add Initial` lalu db.Database.MigrateAsync().
        await db.Database.EnsureCreatedAsync();

        if (await db.Users.AnyAsync()) return;
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var tokens = sp.GetRequiredService<ITokenService>();

        var adminUser = cfg["Tactical:BootstrapAdmin:Username"] ?? "admin";
        var adminPass = cfg["Tactical:BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(adminPass) || adminPass.Length < 12)
            throw new InvalidOperationException("Set Tactical:BootstrapAdmin:Password (min 12 karakter) untuk pembuatan admin pertama.");

        db.Users.Add(new User { Username = adminUser, PasswordHash = hasher.Hash(adminPass), IsSuperuser = true });
        var installer = new User { Username = "installer-user", PasswordHash = hasher.Hash(Guid.NewGuid().ToString("N")), IsInstallerUser = true };
        db.Users.Add(installer);
        await db.SaveChangesAsync();

        // Token installer dipakai di perintah install agent; tampil sekali di log.
        var tok = await tokens.CreateAgentTokenAsync(installer.Id);
        sp.GetRequiredService<ILogger<Program>>().LogWarning("Installer token (simpan, hanya tampil sekali): {Token}", tok);
    }
}
