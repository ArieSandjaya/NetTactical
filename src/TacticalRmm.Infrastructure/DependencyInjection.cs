using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Infrastructure.Jobs;
using TacticalRmm.Infrastructure.Messaging;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Services;

namespace TacticalRmm.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddOptions<TacticalOptions>().Bind(cfg.GetSection("Tactical"))
            .Validate(o => o.SecretKey.Length >= 32, "Tactical:SecretKey wajib diisi minimal 32 karakter")
            .ValidateOnStart();

        s.AddDbContext<AppDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Default")));

        s.AddSingleton<INatsConnection>(sp =>
        {
            var t = sp.GetRequiredService<IOptions<TacticalOptions>>().Value;
            return new NatsConnection(new NatsOpts
            {
                Url = t.NatsUrl,
                Name = "trmm-dotnet",
                AuthOpts = new NatsAuthOpts { Username = "tacticalrmm", Password = t.SecretKey },
                ConnectTimeout = TimeSpan.FromSeconds(3),
            });
        });
        s.AddSingleton<IAgentCommandBus, NatsCommandBus>();

        s.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        s.AddScoped<ITokenService, TokenService>();
        s.AddScoped<IAuthService, AuthService>();
        s.AddScoped<IPermissionService, PermissionService>();
        s.AddScoped<IAuditService, AuditService>();
        s.AddScoped<INatsConfigWriter, NatsConfigWriter>();
        s.AddScoped<IAgentService, AgentService>();
        s.AddScoped<ICheckService, CheckService>();
        s.AddScoped<ITaskService, TaskService>();
        s.AddScoped<IAlertService, AlertService>();
        s.AddScoped<IWinUpdateService, WinUpdateService>();
        s.AddScoped<IAgentTelemetryService, AgentTelemetryService>();
        s.AddHttpClient("mesh");
        s.AddScoped<IMeshService, MeshService>();
        s.AddScoped<ICheckAdminService, CheckAdminService>();
        s.AddScoped<ITaskSyncService, TaskSyncService>();
        s.AddScoped<ITaskAdminService, TaskAdminService>();
        s.AddScoped<IUserAdminService, UserAdminService>();
        s.AddScoped<IRoleAdminService, RoleAdminService>();
        s.AddScoped<IOrgAdminService, OrgAdminService>();
        s.AddScoped<IPolicyAdminService, PolicyAdminService>();
        s.AddHostedService<AgentOutagesJob>();
        s.AddHostedService<TaskSyncJob>();
        return s;
    }
}
