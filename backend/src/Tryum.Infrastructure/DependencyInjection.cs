using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Interfaces;
using Tryum.Domain.Users;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;
using Tryum.Infrastructure.Persistence.Seeding;
using Tryum.Infrastructure.Services;

namespace Tryum.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký tất cả service của Infrastructure layer vào DI container.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' không được tìm thấy trong cấu hình.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)
            ));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

        // ── Options ─────────────────────────────────────────────────────────
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey),
                "Jwt:SecretKey chưa được cấu hình (ít nhất 32 byte base64).")
            .Validate(o => o.ExpiresInMinutes > 0 && o.ExpiresInMinutes <= 1440,
                "Jwt:ExpiresInMinutes phải trong khoảng 1–1440.")
            .ValidateOnStart();

        // ── Services ────────────────────────────────────────────────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IOtpService, OtpService>();
        services.AddScoped<ITokenService>(sp =>
            new TokenService(
                sp.GetRequiredService<IApplicationDbContext>(),
                sp.GetRequiredService<IOptions<JwtOptions>>().Value));

        // ── SMS sender: Provider = "Log" (dev/demo, 0đ) hoặc "SmsGate" (self-host, MIỄN PHÍ) ──
        services.AddOptions<SmsOptions>()
            .Bind(configuration.GetSection(SmsOptions.SectionName));

        var smsProvider = configuration[$"{SmsOptions.SectionName}:Provider"] ?? "Log";

        if (string.Equals(smsProvider, "SmsGate", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(SmsGateSmsSender.ClientName, (sp, http) =>
            {
                var sms = sp.GetRequiredService<IOptions<SmsOptions>>().Value;
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, sms.SmsGate.TimeoutSeconds));
            });

            services.AddSingleton<ISmsSender, SmsGateSmsSender>();
        }
        else
        {
            services.AddSingleton<ISmsSender, LogSmsSender>();
        }

        // Health check cho PostgreSQL qua EF Core
        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>(name: "postgres");

        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}