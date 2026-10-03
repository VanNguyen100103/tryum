using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Common;

/// <summary>
/// Đọc tenant_id từ JWT và đặt vào <see cref="TenantContext"/> cho request.
/// Đặt sau UseAuthentication, trước UseAuthorization nên mọi handler đều thấy đúng tenant.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var tenantClaim = context.User.FindFirst("tenant_id")?.Value;

        TenantContext.Current = Guid.TryParse(tenantClaim, out var tenantId)
            ? tenantId
            : null;

        await next(context);
    }
}