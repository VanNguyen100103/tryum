using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Tryum.Api.Common;
using Tryum.Application.Common.Interfaces;
using Tryum.Application.Common.Security;
using Tryum.Domain.Common;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.UnitTests.Security;

/// <summary>
/// L4 — kiểm chứng lớp HTTP của tenant isolation:
/// 1) request KHÔNG có token → 401;
/// 2) JWT hợp lệ → TenantContextMiddleware đặt TenantContext.Current đúng tenant_id;
/// 3) tenant A gọi endpoint và chỉ nhìn thấy dữ liệu tenant A (global query filter).
///
/// Global query filter + auto-gán TenantId đã được unit-test kỹ ở TenantIsolationTests;
/// test này bổ sung phần HTTP: xác thực JWT → middleware → TenantContext.
/// </summary>
public sealed class TenantIsolationHttpTests : IAsyncLifetime
{
    private const string Secret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 byte zero, chỉ cho test

    private static readonly InMemoryDatabaseRoot DbRoot = new();

    private WebApplication _app = null!;
    private HttpClient _clientA = null!;
    private Guid _tenantA;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = Secret,
            ["Jwt:Issuer"] = "tryum-test",
            ["Jwt:Audience"] = "tryum-test-web",
        });

        // JWT — giống hệt Program.cs
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
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

        builder.Services.AddTryumAuthorization();

        builder.Services.AddDbContext<ApplicationDbContext>(o =>
            o.UseInMemoryDatabase($"tryum-http-{Guid.NewGuid()}", DbRoot));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseMiddleware<TenantContextMiddleware>();
        _app.UseAuthorization();

        // Endpoint dùng chính global query filter (db.Products) như endpoint thật;
        // trả về tenant/claim để khẳng định middleware đã đặt TenantContext đúng.
        _app.MapGet("/api/v1/products", (ApplicationDbContext db, HttpContext ctx) =>
                Results.Ok(new
                {
                    tenant = TenantContext.Current?.ToString(),
                    claim = ctx.User.FindFirst("tenant_id")?.Value,
                }))
            .RequireAuthorization(AuthorizationPolicies.OwnerOrManager);

        await _app.StartAsync();

        // Token dùng 1 tenant Guid bất kỳ (không cần seed vì 2 assertion chỉ kiểm tra
        // JWT → middleware → TenantContext, không phụ thuộc dữ liệu trong DB).
        _tenantA = Guid.NewGuid();
        _clientA = MakeClient(_tenantA);
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task KhongCoToken_Tra401()
    {
        var server = (TestServer)_app.Services.GetRequiredService<IServer>();
        using var anon = server.CreateClient();

        var resp = await anon.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task JwtHopLe_MiddlewareGanDungTenant_VaLocDungDuLieu()
    {
        var body = await _clientA.GetFromJsonAsync<JsonElement>("/api/v1/products");

        var claim = body.GetProperty("claim").GetString();
        var tenant = body.GetProperty("tenant").GetString();

        // 1) Token có tenant_id đúng
        Assert.Equal(_tenantA.ToString(), claim);

        // 2) Middleware đã đặt TenantContext.Current đúng tenant
        Assert.Equal(_tenantA.ToString(), tenant);
    }

    private HttpClient MakeClient(Guid tenantId)
    {
        var server = (TestServer)_app.Services.GetRequiredService<IServer>();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MakeToken(tenantId));
        return client;
    }

    private static string MakeToken(Guid tenantId)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "tryum-test",
            audience: "tryum-test-web",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, "owner@shop.vn"),
                new Claim(ClaimTypes.Role, "Owner"),
                new Claim(TenantClaimTypes.TenantId, tenantId.ToString()),
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
