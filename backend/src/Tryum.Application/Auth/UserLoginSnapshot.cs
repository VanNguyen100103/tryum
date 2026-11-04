using Tryum.Domain.Users;

namespace Tryum.Application.Auth;

/// <summary>
/// Snapshot user phục vụ login, cache trong Redis để bỏ 1 query Postgres (~150–500ms).
/// Chỉ cache dữ liệu cần cho login: hash mật khẩu + role + tenant + trạng thái active.
/// TTL ngắn (5 phút) và không có endpoint đổi mật khẩu hiện tại nên rủi ro stale thấp.
/// </summary>
public sealed record UserLoginSnapshot(
    Guid Id,
    string Email,
    string PasswordHash,
    string FullName,
    string? Phone,
    bool IsActive,
    Guid TenantId,
    Guid? RoleId,
    string? RoleCode,
    string? RoleName,
    string? TenantName,
    string? TenantSlug,
    bool TenantIsActive)
{
    public static UserLoginSnapshot From(User user) => new(
        user.Id,
        user.Email,
        user.PasswordHash,
        user.FullName,
        user.Phone,
        user.IsActive,
        user.TenantId,
        user.RoleId,
        user.Role?.Code,
        user.Role?.Name,
        user.Tenant?.Name,
        user.Tenant?.Slug,
        user.Tenant?.IsActive ?? false);

    public User ToUser() => User.FromCache(
        Id,
        Email,
        PasswordHash,
        FullName,
        Phone,
        IsActive,
        TenantId,
        RoleId,
        RoleCode,
        RoleName,
        TenantName,
        TenantSlug,
        TenantIsActive);
}
