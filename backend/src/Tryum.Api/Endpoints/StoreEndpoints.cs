using Microsoft.EntityFrameworkCore;
using Tryum.Application.Common.Security;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

/// <summary>Thông tin cửa hàng dùng cho hóa đơn in (tên, địa chỉ, điện thoại).</summary>
public static class StoreEndpoints
{
    public static IEndpointRouteBuilder MapStoreEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/store", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var tenantId = TenantContext.RequireCurrent();
            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == tenantId, ct);
            if (tenant is null)
                return Results.NotFound(new { message = "Không tìm thấy cửa hàng." });

            return Results.Ok(new StoreResponse(
                tenant.Name,
                tenant.StoreCode,
                tenant.Phone,
                tenant.Address));
        })
        .RequireAuthorization(AuthorizationPolicies.Authenticated)
        .WithTags("Store")
        .WithSummary("Thông tin cửa hàng hiện tại (in hóa đơn)")
        .Produces<StoreResponse>();

        return app;
    }

    public sealed record StoreResponse(
        string Name,
        string? StoreCode,
        string? Phone,
        string? Address);
}
