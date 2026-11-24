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
            .Validate(o => o.SecretKey.Length >= 32, "Tactical:SecretKey is required and must be at least 32 characters")
            .ValidateOnStart();

        s.AddDbContext<AppDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Default"), n => n.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        s.AddSingleton<INatsConnection>(sp =>
        {
            var t = sp.GetRequiredService<IOptions<TacticalOptions>>().Value;
            return new NatsConnection(new NatsOpts
            {
                Url = t.NatsUrl,
                Name = "trmm-dotnet",
                AuthOpts = new NatsAuthOpts { Username = "tacticalrmm", Password = t.SecretKey },
                ConnectTimeout = TimeSpan.FromSeconds(3),
                // The server reaches NATS over the internal network by the name "nats", which never matches the public certificate. With NATS TLS on, the
                // server may even require TLS. Auto uses TLS when the server offers it (and plain otherwise); for a nats:// URL the certificate name is not
                // checked, because the hop is inside the Docker network. A tls:// URL keeps full verification.
                TlsOpts = t.NatsUrl.StartsWith("tls://", StringComparison.OrdinalIgnoreCase)
                    ? new NatsTlsOpts { Mode = TlsMode.Require }
                    : new NatsTlsOpts { Mode = TlsMode.Auto, InsecureSkipVerify = true },
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
        s.AddScoped<ICheckAdminService, CheckAdminService>();
        s.AddScoped<ITaskSyncService, TaskSyncService>();
        s.AddScoped<ITaskAdminService, TaskAdminService>();
        s.AddScoped<IUserAdminService, UserAdminService>();
        s.AddScoped<IRoleAdminService, RoleAdminService>();
        s.AddScoped<IOrgAdminService, OrgAdminService>();
        s.AddScoped<IPolicyAdminService, PolicyAdminService>();
        s.AddScoped<IAlertTemplateAdminService, AlertTemplateAdminService>();
        s.AddScoped<ICustomFieldService, CustomFieldService>();
        s.AddScoped<ISnippetService, SnippetService>();
        s.AddScoped<IReportService, ReportService>();
        s.AddScoped<ISoftwareService, SoftwareService>();
        s.AddScoped<IVncService, VncService>();
        s.AddScoped<IInstallerService, InstallerService>();
        s.AddScoped<IWinUpdateAdminService, WinUpdateAdminService>();
        s.AddHostedService<WinUpdateScheduler>();
        s.AddSingleton<IRemoteExecutor, Push.PowerShellRemoteExecutor>();
        s.AddSingleton<IPushInstallService, Push.PushInstallService>();
        s.AddSingleton<IBulkService, BulkService>();
        s.AddScoped<IAgentLogService, AgentLogService>();
        s.AddScoped<IRemoteToolsService, RemoteToolsService>();
        s.AddScoped<IAgentUpdateService, AgentUpdateService>();
        s.AddScoped<IFileBrowserService, FileBrowserService>();
        s.AddScoped<IEventLogService, EventLogService>();
        s.AddScoped<IDeploymentService, DeploymentService>();
        s.AddScoped<IAppDeploymentService, AppDeploymentService>();
        s.AddHostedService<AppDeploymentJob>();
        s.AddScoped<IServerMaintenanceService, ServerMaintenanceService>();
        s.AddScoped<IUrlActionService, UrlActionService>();
        s.AddScoped<IHardwareService, HardwareService>();
        s.AddScoped<INotificationAdminService, NotificationAdminService>();
        s.AddSingleton<Notifications.NotificationQueue>();
        s.AddSingleton<INotificationQueue>(sp => sp.GetRequiredService<Notifications.NotificationQueue>());
        s.AddHostedService<Notifications.NotificationWorker>();
        s.AddScoped<IFleetService, FleetService>();
        s.AddHostedService<AgentOutagesJob>();
        s.AddHostedService<TaskSyncJob>();
        s.AddHostedService<MetricRetentionJob>();
        s.AddHostedService<HardwareRefreshJob>();
        return s;
    }
}
