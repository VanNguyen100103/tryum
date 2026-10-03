using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using System.Threading.RateLimiting;
using Tryum.Api.Common;
using Tryum.Api.Endpoints;
using Tryum.Application;
using Tryum.Application.Common.Security;
using Tryum.Infrastructure;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;
using Tryum.Infrastructure.Persistence.Seeding;

// ── Serilog: cấu hình ngay từ đầu để log cả lỗi khởi động ──────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Đang khởi động ứng dụng Tryum API...");

    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog tích hợp ASP.NET Core ────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}"));

    // ── Services ──────────────────────────────────────────────────────────────
    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration);

    builder.Services.AddTryumAuthorization();
    builder.Services.AddExceptionHandler<AppExceptionHandler>();

    // ── JWT ───────────────────────────────────────────────────────────────────
    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
        ?? throw new InvalidOperationException("Thiếu cấu hình Jwt trong appsettings.");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                ClockSkew = TimeSpan.FromSeconds(30),
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SecretKey)),
            };
        });

    // Rate limiting cho endpoint đăng nhập / đăng ký (chống brute force)
    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("auth", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
    });

    // ── CORS (Cloudflare Pages ở prod) ────────────────────────────────────────
    const string CorsPolicy = "spa";
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
        .Where(origin => !string.IsNullOrWhiteSpace(origin)).ToArray()
        ?? ["http://localhost:4200"];

    builder.Services.AddCors(options =>
        options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));

    // OpenAPI + Scalar
    builder.Services.AddOpenApi();

    // ProblemDetails – chuẩn RFC 9457
    builder.Services.AddProblemDetails();

    // ── Build app ─────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseExceptionHandler();          // Global error handler → ProblemDetails
    app.UseStatusCodePages();           // 404, 405... → ProblemDetails

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} => {StatusCode} ({Elapsed:0.0}ms)";
    });

    app.UseCors(CorsPolicy);
    app.UseRateLimiter();

    app.UseAuthentication();                       // đọc JWT → HttpContext.User
    app.UseMiddleware<TenantContextMiddleware>();  // gán TenantContext từ claim
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();               // /openapi/v1.json
        app.MapScalarApiReference();    // /scalar/v1
    }

    app.UseHttpsRedirection();

    // ── Endpoints ─────────────────────────────────────────────────────────────
    app.MapHealthChecks("/health");
    app.MapAuthEndpoints();

    // ── Database: migrate + seed khi khởi động (mặc định bật ở Development) ───
    if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", app.Environment.IsDevelopment()))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        Log.Information("Đã áp dụng migration và seed dữ liệu khởi tạo.");
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Ứng dụng bị dừng do lỗi không xử lý được.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

/// <summary>Marker để WebApplicationFactory (integration test) tìm thấy entry point.</summary>
public partial class Program;