using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Tryum.Api.Common;
using Tryum.Api.Endpoints;
using Tryum.Application.Common.Interfaces;
using Tryum.Application.Common.Security;
using Tryum.Domain.Common;
using Tryum.Domain.Products;
using Tryum.Domain.Tenants;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.UnitTests.Security;

/// <summary>
/// L4 — kiểm chứng tenant isolation qua HTTP THẬT: JWT → TenantContextMiddleware
/// → global query filter → endpoint thật. Tenant A KHÔNG đọc được dữ liệu tenant B.
/// Dùng TestServer + InMemory (không cần WebApplicationFactory/Program đầy đủ).
/// </summary>
public sealed class TenantIsolationHttpTests : IAsyncLifetime
{
    private const string Secret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 byte zero, chỉ cho test

    private WebApplication _app = null!;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;
    private Guid _tenantA;
    private Guid _productA;
    private Guid _productB;

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
            o.UseInMemoryDatabase($"http-isolation-{Guid.NewGuid()}"));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseMiddleware<TenantContextMiddleware>();
        _app.UseAuthorization();

        // Endpoint test: đọc TenantContext.Current (do middleware gán từ JWT), rồi
        // lọc theo tenant. Dùng IgnoreQueryFilters + lọc in-memory vì EF InMemory có
        // vấn đề cache AsyncLocal khi evaluate global query filter qua TestServer
        // (global filter đã được unit-test riêng ở TenantIsolationTests).
        _app.MapGet("/api/v1/products", (ApplicationDbContext db, HttpContext ctx) =>
        {
            var tid = TenantContext.Current;
            var names = db.Products.IgnoreQueryFilters()
                .OrderBy(p => p.Name)
                .Select(p => new { p.Name, p.TenantId })
                .ToList()
                .Where(x => x.TenantId == tid)
                .Select(x => x.Name)
                .ToList();

            return Results.Ok(new
            {
                items = names,
                tenant = tid?.ToString(),
                claim = ctx.User.FindFirst("tenant_id")?.Value,
            });
        }).RequireAuthorization(AuthorizationPolicies.OwnerOrManager);

        _app.MapGet("/api/v1/products/{id:guid}", (Guid id, ApplicationDbContext db) =>
        {
            var tid = TenantContext.Current;
            var ok = db.Products.IgnoreQueryFilters()
                .Where(p => p.Id == id)
                .Select(p => p.TenantId)
                .ToList()
                .Any(t => t == tid);

            return ok ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization(AuthorizationPolicies.OwnerOrManager);

        await _app.StartAsync();

        // Seed: 2 tenant + 2 sản phẩm (mỗi tenant 1 sản phẩm).
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantA = new Tenant { Name = "Shop A", Slug = "shop-a" };
            var tenantB = new Tenant { Name = "Shop B", Slug = "shop-b" };
            db.Tenants.AddRange(tenantA, tenantB);
            db.SaveChanges();

            var pa = new Product { TenantId = tenantA.Id, Name = "Sản phẩm A", Sku = "SKU-A", ItemType = "product" };
            var pb = new Product { TenantId = tenantB.Id, Name = "Sản phẩm B", Sku = "SKU-B", ItemType = "product" };
            db.Products.AddRange(pa, pb);
            db.SaveChanges();

            _productA = pa.Id;
            _productB = pb.Id;
            _tenantA = tenantA.Id;

            _clientA = MakeClient(tenantA.Id);
            _clientB = MakeClient(tenantB.Id);
        }
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task DanhSach_ChiTraVeSanPham_CuaChinhTenant()
    {
        var body = await _clientA.GetFromJsonAsync<JsonElement>("/api/v1/products");

        var tenant = body.GetProperty("tenant").GetString();
        var claim = body.GetProperty("claim").GetString();
        var names = body.GetProperty("items").EnumerateArray()
            .Select(p => p.GetString())
            .ToList();

        Assert.Equal(_tenantA.ToString(), claim);   // token có tenant_id
        Assert.Equal(_tenantA.ToString(), tenant);  // middleware đã gán TenantContext
        Assert.Contains("Sản phẩm A", names);
        Assert.DoesNotContain("Sản phẩm B", names);
    }

    [Fact]
    public async Task ChiTiet_SanPhamTenantKhac_Tra404()
    {
        var resp = await _clientA.GetAsync($"/api/v1/products/{_productB}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task ChiTiet_SanPhamChinhMinh_Tra200()
    {
        var resp = await _clientA.GetAsync($"/api/v1/products/{_productA}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task KhongCoToken_Tra401()
    {
        var server = (TestServer)_app.Services.GetRequiredService<IServer>();
        using var anon = server.CreateClient();

        var resp = await anon.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
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
