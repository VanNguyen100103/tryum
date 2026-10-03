using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tryum.Application.Common.Interfaces;
using Tryum.Domain.Common;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// Truy xuất user hiện tại từ ClaimsPrincipal (set bởi JWT bearer middleware).
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly HttpContext? _context;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _context = httpContextAccessor.HttpContext;
    }

    // ── ICurrentUserService ────────────────────────────────────────────────
    private ClaimsPrincipal? Principal => _context?.User;

    public Guid? UserId => ReadGuid(ClaimTypes.NameIdentifier);

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public Guid? TenantId => ReadGuid(TenantClaimTypes.TenantId);

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public bool IsInRole(string roleCode) =>
        Principal is not null && Principal.IsInRole(roleCode);

    ClaimsPrincipal? ICurrentUserService.Principal => Principal;

    /// <summary>Tenant hiện tại, ném lỗi nếu request chưa đăng nhập.</summary>
    Guid ICurrentUserService.RequireTenantId() =>
        TenantId ?? throw new InvalidOperationException(
            "Request chưa xác định được tenant – token không hợp lệ hoặc thiếu claim tenant_id.");

    /// <summary>User hiện tại, ném lỗi nếu request chưa đăng nhập.</summary>
    Guid ICurrentUserService.RequireUserId() =>
        UserId ?? throw new InvalidOperationException(
            "Request chưa xác định được user – token không hợp lệ hoặc thiếu claim.");

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(Principal?.FindFirstValue(claimType), out var value) ? value : null;
}