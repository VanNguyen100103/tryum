using Microsoft.EntityFrameworkCore;
using Tryum.Domain.Tenants;
using Tryum.Domain.Users;
using Tryum.Infrastructure.Persistence;

namespace Tryum.UnitTests.Persistence;

/// <summary>
/// Kiểm chứng global query filter: tenant A không bao giờ đọc được dữ liệu tenant B.
/// Đây là "hàng rào" quan trọng nhất của kiến trúc multi-tenant.
/// </summary>
public class TenantIsolationTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly Guid _tenantA;
    private readonly Guid _tenantB;

    public TenantIsolationTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"isolation-{Guid.NewGuid()}")
            .Options;

        _context = new TestDbContext(options);

        _tenantA = SeedTenant("a@shop.vn", "Chủ A");
        _tenantB = SeedTenant("b@shop.vn", "Chủ B");
    }

    public void Dispose()
    {
        _context.Dispose();
        TenantContext.Current = null;
        GC.SuppressFinalize(this);
    }

    private Guid SeedTenant(string email, string fullName)
    {
        var tenant = new Tenant { Name = $"Shop {email}", Slug = email };
        _context.Tenants.Add(tenant);
        _context.Users.Add(new User
        {
            TenantId = tenant.Id,
            Email = email,
            FullName = fullName,
            PasswordHash = "hash",
        });

        _context.SaveChanges();

        return tenant.Id;
    }

    [Fact]
    public async Task QueryFilter_ShouldReturnOnlyCurrentTenant_Users()
    {
        using var scope = TenantContext.Use(_tenantA);

        var users = await _context.Users.ToListAsync();

        var user = Assert.Single(users);
        Assert.Equal("a@shop.vn", user.Email);
    }

    [Fact]
    public async Task QueryFilter_ShouldReturnOnlyCurrentTenant_RefreshTokens()
    {
        _context.RefreshTokens.Add(new RefreshToken
        {
            TenantId = _tenantA,
            UserId = Guid.NewGuid(),
            Token = "token-a",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        _context.RefreshTokens.Add(new RefreshToken
        {
            TenantId = _tenantB,
            UserId = Guid.NewGuid(),
            Token = "token-b",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        _context.SaveChanges();

        using var scope = TenantContext.Use(_tenantA);

        var tokens = await _context.RefreshTokens.ToListAsync();

        Assert.Equal("token-a", Assert.Single(tokens).Token);
    }

    [Fact]
    public async Task QueryFilter_ShouldReturnNothing_WhenNoTenantInContext()
    {
        TenantContext.Current = null;

        Assert.Empty(await _context.Users.ToListAsync());
    }

    [Fact]
    public async Task IgnoreQueryFilters_ShouldSeeAllTenants()
    {
        TenantContext.Current = null;

        Assert.Equal(2, await _context.Users.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SaveChanges_ShouldAssignCurrentTenant_ToNewEntities()
    {
        TenantContext.Current = _tenantA;

        _context.Products.Add(new TestProduct { Name = "Bún riêu" });
        await _context.SaveChangesAsync();

        Assert.Equal(_tenantA, _context.ChangeTracker.Entries<TestProduct>().Single().Entity.TenantId);
    }

    [Fact]
    public async Task SaveChanges_ShouldThrow_WhenNoTenantAndEntityHasNoTenantId()
    {
        TenantContext.Current = null;
        _context.Products.Add(new TestProduct { Name = "Không tenant" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public void TenantContext_Use_ShouldRestorePreviousValue()
    {
        TenantContext.Current = _tenantA;

        using (TenantContext.Use(_tenantB))
            Assert.Equal(_tenantB, TenantContext.Current);

        Assert.Equal(_tenantA, TenantContext.Current);
    }

    /// <summary>Entity tenant-owned dùng riêng cho test (mô phỏng hàng hóa phase 3).</summary>
    private sealed class TestProduct : Domain.Common.AuditableEntity, Domain.Common.ITenantOwned
    {
        public string Name { get; set; } = string.Empty;

        public Guid TenantId { get; set; }
    }

    /// <summary>Context test: đăng ký thêm <see cref="TestProduct"/> vào model.</summary>
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : ApplicationDbContext(options)
    {
        public DbSet<TestProduct> Products => Set<TestProduct>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TestProduct>();
        }
    }
}