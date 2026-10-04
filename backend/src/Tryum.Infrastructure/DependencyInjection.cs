using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        // ── Options: OTP + Captcha (chống spam/quota) ──────────────────────────
        services
            .AddOptions<OtpOptions>()
            .Bind(configuration.GetSection(OtpOptions.SectionName))
            .Validate(o => o.TtlSeconds > 0, "Otp:TtlSeconds phải > 0.")
            .Validate(o => o.CooldownSeconds >= 0, "Otp:CooldownSeconds phải >= 0.")
            .Validate(o => o.MaxAttempts > 0, "Otp:MaxAttempts phải > 0.")
            .Validate(o => o.MaxSendsPerDay > 0, "Otp:MaxSendsPerDay phải > 0.")
            .ValidateOnStart();

        services
            .AddOptions<CaptchaOptions>()
            .Bind(configuration.GetSection(CaptchaOptions.SectionName))
            .Validate(o => o.TtlSeconds > 0, "Captcha:TtlSeconds phải > 0.")
            .ValidateOnStart();

        // ── Services ────────────────────────────────────────────────────────────
        services.AddSingleton<IOtpService>(sp =>
            new OtpService(
                sp.GetRequiredService<ISmsSender>(),
                sp.GetService<ILogger<OtpService>>(),
                sp.GetRequiredService<IOptions<OtpOptions>>().Value));

        // Captcha singleton: store dùng chung cho cả sinh ảnh và kiểm mã.
        services.AddSingleton<ICaptchaService>(sp =>
            new CaptchaService(sp.GetRequiredService<IOptions<CaptchaOptions>>().Value));
        services.AddScoped<ITokenService>(sp =>
            new TokenService(
                sp.GetRequiredService<IApplicationDbContext>(),
                sp.GetRequiredService<IOptions<JwtOptions>>().Value));

        // ── SMS sender: Provider = "Log" (dev/demo, 0đ) | "SmsGate" | "Textbee" (self-host, MIỄN PHÍ) ──
        // Fail-fast: nếu chọn SmsGate mà thiếu SendUrl/Username/Password, hoặc chọn Textbee mà thiếu
        // ApiKey, thì API báo lỗi ngay lúc khởi động (không để production âm thầm chạy mà không
        // gửi được SMS).
        services.AddOptions<SmsOptions>()
            .Bind(configuration.GetSection(SmsOptions.SectionName))
            .Validate(
                o => !string.Equals(o.Provider, "SmsGate", StringComparison.OrdinalIgnoreCase)
                     || (!string.IsNullOrWhiteSpace(o.SmsGate.SendUrl)
                         && !string.IsNullOrWhiteSpace(o.SmsGate.Username)
                         && !string.IsNullOrWhiteSpace(o.SmsGate.Password)),
                "Sms:Provider=SmsGate yêu cầu Sms:SmsGate:SendUrl + Username + Password. " +
                "Xem SMS_SETUP.md và deploy/.env.example.")
            .Validate(
                o => !string.Equals(o.Provider, "Textbee", StringComparison.OrdinalIgnoreCase)
                     || (!string.IsNullOrWhiteSpace(o.Textbee.ApiKey)
                         && !string.IsNullOrWhiteSpace(o.Textbee.SendUrl)),
                "Sms:Provider=Textbee yêu cầu Sms:Textbee:ApiKey + SendUrl. " +
                "Xem SMS_SETUP.md và deploy/.env.example.")
            .ValidateOnStart();

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
        else if (string.Equals(smsProvider, "Textbee", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(TextbeeSmsSender.ClientName, (sp, http) =>
            {
                var sms = sp.GetRequiredService<IOptions<SmsOptions>>().Value;
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, sms.Textbee.TimeoutSeconds));
            });

            services.AddSingleton<ISmsSender, TextbeeSmsSender>();
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