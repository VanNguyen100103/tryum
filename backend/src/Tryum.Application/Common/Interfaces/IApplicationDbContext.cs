using Microsoft.EntityFrameworkCore;
using Tryum.Domain.Common;

namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Contract cho EF Core DbContext, dùng trong Application layer.
/// Tránh phụ thuộc trực tiếc vào Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    /// <summary>Truy cập DbSet theo entity type (kèm global query filter multi-tenant).</summary>
    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    /// <summary>Lưu thay đổi + tự động set audit timestamp / TenantId.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}