using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tryum.Application.Common.Interfaces;
using Tryum.Domain.Common;
using Tryum.Domain.Tenants;
using Tryum.Domain.Users;

namespace Tryum.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext chính của ứng dụng.
/// - Tự động set CreatedAt / UpdatedAt trước khi save.
/// - Tự động gán TenantId cho entity mới thuộc tenant.
/// - Implements IApplicationDbContext để Application layer không phụ thuộc Infrastructure.
/// - Global query filter: tự động lọc entity ITenantOwned theo tenant hiện tại (multi-tenant).
/// </summary>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    protected ApplicationDbContext(DbContextOptions options)
        : base(options) { }

    // ── DbSets ──────────────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tự động áp dụng tất cả IEntityTypeConfiguration trong assembly này
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // ── Multi-tenant global query filter ────────────────────────────────
        // Mỗi entity implement ITenantOwned sẽ tự động bị lọc theo tenant hiện tại.
        // Filter này được áp dụng cho TEntity khi T implements ITenantOwned.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType is not null
                && typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(ApplicationDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(entityType.ClrType);

                method.Invoke(null, [modelBuilder]);
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    private static void ApplyTenantFilter<T>(ModelBuilder modelBuilder)
        where T : class, ITenantOwned
    {
        // Luôn lọc theo tenant hiện tại. Khi chưa đăng nhập (TenantContext.Current == null)
        // không entity nào khớp => mặc định "không thấy gì", an toàn hơn là trả về tất cả.
        // Code cần đọc/ghi ngoài phạm vi tenant (đăng nhập, kiểm tra email trùng, refresh token)
        // phải gọi IgnoreQueryFilters() một cách tường minh.
        modelBuilder.Entity<T>()
            .HasQueryFilter(e => e.TenantId == TenantContext.Current);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenantAndAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTenantAndAudit();
        return base.SaveChanges();
    }

    /// <summary>
    /// Gán TenantId cho entity mới + cập nhật audit timestamps trước khi ghi xuống DB.
    /// </summary>
    private void ApplyTenantAndAudit()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not EntityState.Added)
                continue;

            if (entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = TenantContext.RequireCurrent();
            }
        }

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;

            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
    }
}