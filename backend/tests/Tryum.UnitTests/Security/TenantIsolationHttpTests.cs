using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Tryum.Application.Common.Interfaces;
using Tryum.Domain.Common;
using Tryum.Domain.Products;
using Tryum.Domain.Tenants;
using Tryum.Infrastructure.Persistence;

namespace Tryum.UnitTests.Security;

/// <summary>
/// L4 — kiểm chứng tenant isolation qua HTTP THẬT (không phải unit test DbContext).
/// Tenant A (JWT hợp lệ) KHÔNG được đọc dữ liệu tenant B, kể cả khi đoán đúng ID.
/// </summary>
public sealed class TenantIsolationHttpTests : IClassFixture<TryumApiFactory>
{
    private const string TestSecret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 byte zero, chỉ cho test

    private readonly TryumApiFactory _factory;
    private readonly HttpClient _clientA;
    private readonly HttpClient _clientB;
    private readonly Guid _productA;
    private readonly Guid _productB;

    public TenantIsolationHttpTests(TryumApiFactory factory)
    {
        _factory = factory;

        using var scope = factory.Services.CreateScope();
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

        _clientA = factory.CreateClient();
        _clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MakeToken(tenantA.Id));

        _clientB = factory.CreateClient();
        _clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MakeToken(tenantB.Id));
    }

    [Fact]
    public async Task DanhSach_ChiTraVeSanPham_CuaChinhTenant()
    {
        var body = await _clientA.GetFromJsonAsync<JsonElement>("/api/v1/products");

        var names = body.GetProperty("items").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .ToList();

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
        var resp = await _factory.CreateClient().GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private static string MakeToken(Guid tenantId)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(TestSecret));
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

/// <summary>
/// Factory: chạy API thật bằng InMemory DB + JWT test, bỏ migration/seed/ngoại lệ ngoài.
/// </summary>
public sealed class TryumApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["Jwt:Issuer"] = "tryum-test",
                ["Jwt:Audience"] = "tryum-test-web",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["OpenTelemetry:Endpoint"] = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Thay Postgres thật bằng InMemory cho test.
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                || d.ServiceType == typeof(ApplicationDbContext)
                || d.ServiceType == typeof(IApplicationDbContext)).ToList();

            foreach (var d in toRemove)
                services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(o =>
                o.UseInMemoryDatabase($"http-isolation-{Guid.NewGuid()}"));

            services.AddScoped<IApplicationDbContext>(sp =>
                sp.GetRequiredService<ApplicationDbContext>());
        });
    }
}
