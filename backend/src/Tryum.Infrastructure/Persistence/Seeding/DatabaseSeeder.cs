using Microsoft.EntityFrameworkCore;
using Tryum.Domain.Users;

namespace Tryum.Infrastructure.Persistence.Seeding;

/// <summary>
/// Nạp dữ liệu khởi tạo: các role dùng chung toàn hệ thống.
/// Chạy idempotent nên gọi ở mỗi lần start API cũng an toàn.
/// </summary>
public sealed class DatabaseSeeder(ApplicationDbContext context)
{
    private static readonly (string Code, string Name, string Description)[] Roles =
    [
        (nameof(RoleCode.Owner), "Chủ cửa hàng", "Toàn quyền trong cửa hàng"),
        (nameof(RoleCode.Manager), "Quản lý", "Quản lý hàng hóa, kho và xem báo cáo"),
        (nameof(RoleCode.Cashier), "Thu ngân", "Bán hàng và tra cứu khách hàng"),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await context.Roles
            .Select(r => r.Code)
            .ToListAsync(cancellationToken);

        var missing = Roles
            .Where(r => !existing.Contains(r.Code))
            .Select(r => new Role
            {
                Code = r.Code,
                Name = r.Name,
                Description = r.Description,
            })
            .ToList();

        if (missing.Count == 0)
            return;

        context.Roles.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }
}