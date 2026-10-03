using Microsoft.EntityFrameworkCore;
using Tryum.Domain.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.UnitTests.Persistence;

/// <summary>Entity dùng riêng cho test audit timestamp.</summary>
internal sealed class TestTenant : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Context test: giữ nguyên logic audit của ApplicationDbContext nhưng
/// đăng ký thêm entity <see cref="TestTenant"/> vào model.
/// </summary>
internal sealed class TestApplicationDbContext : ApplicationDbContext
{
    public TestApplicationDbContext(DbContextOptions<TestApplicationDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TestTenant>();
    }
}

public class ApplicationDbContextTests
{
    private static TestApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase($"test-{Guid.NewGuid()}")
            .Options;

        return new TestApplicationDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldStampCreatedAtAndUpdatedAt_OnAdd()
    {
        await using var context = CreateContext();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var tenant = new TestTenant { Name = "Cửa hàng Test" };
        context.Set<TestTenant>().Add(tenant);
        await context.SaveChangesAsync();

        Assert.NotEqual(default, tenant.CreatedAt);
        Assert.True(tenant.CreatedAt >= before);
        Assert.True(tenant.UpdatedAt >= before);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldKeepCreatedAt_MoveUpdatedAtForward_OnModify()
    {
        await using var context = CreateContext();

        var tenant = new TestTenant { Name = "Cửa hàng Test" };
        context.Set<TestTenant>().Add(tenant);
        await context.SaveChangesAsync();

        var createdAt = tenant.CreatedAt;

        tenant.Name = "Cửa hàng Đổi Tên";
        await context.SaveChangesAsync();

        Assert.Equal(createdAt, tenant.CreatedAt);
        Assert.True(tenant.UpdatedAt >= createdAt);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldReturnRowsAffected()
    {
        await using var context = CreateContext();

        context.Set<TestTenant>().Add(new TestTenant { Name = "A" });
        context.Set<TestTenant>().Add(new TestTenant { Name = "B" });

        var affected = await context.SaveChangesAsync();

        Assert.Equal(2, affected);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldPersistChanges()
    {
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase($"persist-{Guid.NewGuid()}")
            .Options;

        await using (var seed = new TestApplicationDbContext(options))
        {
            seed.Set<TestTenant>().Add(new TestTenant { Name = "Cửa hàng X" });
            await seed.SaveChangesAsync();
        }

        await using var verify = new TestApplicationDbContext(options);
        var saved = await verify.Set<TestTenant>().SingleAsync();

        Assert.Equal("Cửa hàng X", saved.Name);
        Assert.NotEqual(default, saved.CreatedAt);
    }
}
