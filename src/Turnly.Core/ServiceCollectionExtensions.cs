using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Turnly.Core.Auth;
using Turnly.Core.Data;
using Turnly.Core.Notifications;
using Turnly.Core.Services;

namespace Turnly.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Turnly domain: the DbContext (SQLite by default, Postgres optional via
    /// <c>Database:Provider</c>), password hashing, token issuance, and business services.
    /// </summary>
    public static IServiceCollection AddTurnlyCore(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.Configure<VapidOptions>(config.GetSection(VapidOptions.SectionName));

        var provider = (config["Database:Provider"] ?? "sqlite").Trim().ToLowerInvariant();
        var connectionString = config.GetConnectionString("Default");

        services.AddDbContext<TurnlyDbContext>(options =>
        {
            if (provider is "postgres" or "postgresql")
                options.UseNpgsql(connectionString ?? "Host=localhost;Database=turnly;Username=turnly;Password=turnly");
            else
                options.UseSqlite(connectionString ?? "Data Source=turnly.db");
        });

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<SetupService>();
        services.AddScoped<TagService>();
        services.AddScoped<ChoreService>();
        services.AddScoped<AwardService>();
        services.AddScoped<RedemptionService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AppSettingsService>();
        services.AddScoped<AchievementService>();
        services.AddScoped<GachaService>();
        services.AddSingleton<IPushSender, WebPushSender>();

        return services;
    }
}
