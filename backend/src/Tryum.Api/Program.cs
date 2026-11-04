using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using Tryum.Api.Common;
using Tryum.Api.Assistant;
using Tryum.Api.Endpoints;
using Tryum.Api.Messaging;
using Tryum.Api.Shipping;
using Tryum.Application;
using Tryum.Application.Common.Interfaces;
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
    builder.Services.AddSingleton<ProductImportJobStore>(sp =>
        new ProductImportJobStore(sp.GetRequiredService<Tryum.Application.Common.Interfaces.IRedisStore>()));
    builder.Services.Configure<GoshipOptions>(builder.Configuration.GetSection(GoshipOptions.SectionName));
    builder.Services.AddHttpClient<GoshipClient>();

    // ── OpenTelemetry → Grafana Cloud (bật khi có endpoint) ─────────────────
    var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
    Log.Information("OpenTelemetry endpoint cấu hình: {Endpoint}", string.IsNullOrWhiteSpace(otelEndpoint) ? "(trống)" : otelEndpoint);
    if (!string.IsNullOrWhiteSpace(otelEndpoint))
    {
        var otelHeaders = builder.Configuration["OpenTelemetry:Headers"];
        var otelService = builder.Configuration["OpenTelemetry:ServiceName"] ?? "tryum-api";
        var otelNamespace = builder.Configuration["OpenTelemetry:ServiceNamespace"] ?? "tryum";
        var otelEnvironment = builder.Configuration["OpenTelemetry:Environment"] ?? "production";
        var otelBasic = otelHeaders?.StartsWith("Authorization=Basic ", StringComparison.OrdinalIgnoreCase) == true
            ? otelHeaders["Authorization=Basic ".Length..].Trim()
            : null;
        HttpClient CreateOtlpClient()
        {
            var client = new HttpClient();
            if (!string.IsNullOrWhiteSpace(otelBasic))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", otelBasic);
            return client;
        }
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(otelService, serviceNamespace: otelNamespace)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = otelEnvironment,
                }))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.SourceName)
                .AddAspNetCoreInstrumentation(options => options.RecordException = true)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otelEndpoint);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    if (!string.IsNullOrWhiteSpace(otelHeaders)) options.Headers = otelHeaders;
                    options.HttpClientFactory = CreateOtlpClient;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otelEndpoint);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    if (!string.IsNullOrWhiteSpace(otelHeaders)) options.Headers = otelHeaders;
                    options.HttpClientFactory = CreateOtlpClient;
                }));
    }

    builder.Services.Configure<AiChatOptions>(builder.Configuration.GetSection(AiChatOptions.SectionName));
    builder.Services.AddHttpClient("ai", client => client.Timeout = TimeSpan.FromSeconds(25));
    builder.Services.AddHttpClient("social", client => client.Timeout = TimeSpan.FromSeconds(30));
    builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
    builder.Services.AddSingleton<RabbitMqPublisher>();
    builder.Services.AddHostedService<OrderEventConsumer>();
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

    // Enum trả về dạng chuỗi ("Cash", "Completed") thay vì số – dễ đọc cho FE/báo cáo.
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    // ── Sau Caddy: tin X-Forwarded-Proto/For để biết request gốc là HTTPS ────
    // Thiếu bước này, UseHttpsRedirection sẽ trả 307 cho webhook SePay (SePay chỉ
    // chấp nhận 200/201) và rate limit ghi nhận sai IP.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // API không expose ra ngoài (chỉ Caddy trong cùng network gọi tới).
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

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

    // Đọc X-Forwarded-* TRƯỚC rate limiter để giới hạn theo IP thật của khách,
    // không phải IP của Caddy (nếu không mọi người dùng chung một bucket).
    app.UseForwardedHeaders();

    var metricsToken = builder.Configuration["Metrics:Token"];
    app.UseWhen(context => context.Request.Path.StartsWithSegments("/metrics"), branch =>
    {
        branch.Use(async (context, next) =>
        {
            if (!string.IsNullOrWhiteSpace(metricsToken) &&
                context.Request.Headers.Authorization != $"Bearer {metricsToken}")
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next();
        });
    });
    app.UseHttpMetrics(options => options.ReduceStatusCodeCardinality());

    // Span tối thiểu cho mọi request, kể cả khi instrumentation ASP.NET Core không hook được.
    app.Use(async (context, next) =>
    {
        using var activity = Telemetry.Source.StartActivity(
            $"{context.Request.Method} {context.Request.Path}",
            System.Diagnostics.ActivityKind.Server);
        activity?.SetTag("http.request.method", context.Request.Method);
        activity?.SetTag("url.path", context.Request.Path);
        try
        {
            await next();
            activity?.SetTag("http.response.status_code", context.Response.StatusCode);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
    });

    app.UseCors(CorsPolicy);

    // ── Rate limiting phân tán qua Redis (dùng chung cho nhiều instance) ─────
    // Thay cho AddRateLimiter/UseRateLimiter in-memory: mỗi instance trước đây tự đếm
    // riêng nên chạy 3 bản sau LB sẽ yếu đi 3 lần. Giờ đếm chung trên Redis.
    var rateLimits = new Dictionary<string, (int Limit, TimeSpan Window)>(StringComparer.Ordinal)
    {
        ["auth"] = (20, TimeSpan.FromMinutes(1)),
        ["assistant"] = (30, TimeSpan.FromMinutes(1)),
    };
    app.Use(async (context, next) =>
    {
        var attr = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        if (attr is null || string.IsNullOrWhiteSpace(attr.PolicyName)
            || !rateLimits.TryGetValue(attr.PolicyName, out var rule))
        {
            await next();
            return;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var redis = context.RequestServices.GetRequiredService<IRedisStore>();
        var key = $"rl:{attr.PolicyName}:{ip}";

        var count = await redis.IncrementAsync(key);
        if (count == 1)
            await redis.SetExpiryAsync(key, rule.Window);

        if (count > rule.Limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ((int)rule.Window.TotalSeconds).ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                title = "TooManyRequests",
                status = StatusCodes.Status429TooManyRequests,
                detail = "Quá nhiều yêu cầu. Vui lòng thử lại sau.",
            });
            return;
        }

        await next();
    });

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
    app.MapMetrics("/metrics");
    app.MapHealthChecks("/health");
    app.MapAuthEndpoints();
    app.MapProductEndpoints();
    app.MapCustomerEndpoints();
    app.MapOrderEndpoints();
    app.MapPaymentEndpoints();
    app.MapPosQrEndpoints();
    app.MapStoreEndpoints();
    app.MapShippingEndpoints();
    app.MapAssistantEndpoints();
    app.MapSocialEndpoints();

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
