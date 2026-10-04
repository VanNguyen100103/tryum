using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tryum.Application.Auth;
using Tryum.Application.Common;
using Tryum.Application.Common.Exceptions;
using Tryum.Application.Common.Interfaces;
using Tryum.Domain.Tenants;
using Tryum.Domain.Users;

namespace Tryum.Application.Auth;

/// <summary>
/// Triển khai nghiệp vụ auth: đăng ký cửa hàng, đăng nhập, refresh token (rotation).
/// </summary>
public sealed class AuthService(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IOtpService otpService) : IAuthService
{
    private static readonly (RoleCode Code, string Name, string Description)[] DefaultRoles =
    [
        (RoleCode.Owner, "Chủ cửa hàng", "Toàn quyền trong cửa hàng"),
        (RoleCode.Manager, "Quản lý", "Quản lý hàng hóa, kho và xem báo cáo"),
        (RoleCode.Cashier, "Thu ngân", "Bán hàng và tra cứu khách hàng"),
    ];

    public async Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        string? ip = null,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);

        // Xác thực OTP trước khi tạo dữ liệu (mã dùng 1 lần, hết hạn tự động).
        await otpService.ConsumeAsync(request.Phone, request.OtpCode, cancellationToken);

        await EnsureEmailAvailableAsync(email, cancellationToken);
        await EnsureRolesExistAsync(cancellationToken);

        // Chuẩn hoá SĐT về E.164 để lưu nhất quán (vd +84901234567).
        var phone = PhoneNormalizer.TryNormalize(request.Phone, out var normalized) ? normalized : request.Phone.Trim();

        var tenant = new Tenant
        {
            Name = request.ShopName.Trim(),
            Slug = await GenerateSlugAsync(request.ShopName, cancellationToken),
            Phone = phone,
        };

        var ownerRole = await GetRoleAsync(RoleCode.Owner, cancellationToken);

        var user = new User
        {
            Email = email,
            FullName = request.OwnerName.Trim(),
            Phone = phone,
            PasswordHash = passwordHasher.Hash(request.Password),
            TenantId = tenant.Id,
            RoleId = ownerRole.Id,
        };

        context.Set<Tenant>().Add(tenant);
        context.Set<User>().Add(user);

        await context.SaveChangesAsync(cancellationToken);

        user.Tenant = tenant;
        user.Role = ownerRole;

        return await IssueTokensAsync(user, ip, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(
        LoginRequest request,
        string? ip = null,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);

        // Chưa có tenant trong context lúc đăng nhập => phải bỏ query filter để tìm user.
        var user = await context.Set<User>()
            .IgnoreQueryFilters()
            .Include(u => u.Role)
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException();

        if (!user.IsActive)
            throw new ForbiddenException("Tài khoản đã bị khóa. Vui lòng liên hệ chủ cửa hàng.");

        if (user.Tenant is { IsActive: false })
            throw new ForbiddenException("Cửa hàng đã ngừng hoạt động.");

        return await IssueTokensAsync(user, ip, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(
        RefreshRequest request,
        string? ip = null,
        CancellationToken cancellationToken = default)
    {
        // Refresh token là chuỗi bí mật, chưa biết tenant => IgnoreQueryFilters.
        var refreshToken = await tokenService.GetActiveRefreshTokenAsync(request.RefreshToken, cancellationToken);

        if (refreshToken is null)
            throw new UnauthorizedException("Refresh token không hợp lệ hoặc đã hết hạn.");

        var user = refreshToken.User;

        if (!user.IsActive || user.Tenant is { IsActive: false })
            throw new ForbiddenException("Tài khoản không còn hoạt động.");

        var rotated = await tokenService.RotateRefreshTokenAsync(refreshToken, ip, cancellationToken);

        return BuildResponse(user, rotated.Token);
    }

    public Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default) =>
        tokenService.RevokeAllRefreshTokensAsync(userId, null, cancellationToken);

    public async Task<UserInfo> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await context.Set<User>()
            .Include(u => u.Role)
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            throw new NotFoundException("Không tìm thấy người dùng.");

        return ToUserInfo(user);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<AuthResponse> IssueTokensAsync(
            User user,
            string? ip,
            CancellationToken cancellationToken)
    {
        var refreshToken = await tokenService.GenerateRefreshTokenAsync(user.Id, user.TenantId, ip, cancellationToken);

        return BuildResponse(user, refreshToken.Token);
    }

    private AuthResponse BuildResponse(User user, string refreshToken)
    {
        return new AuthResponse(
            tokenService.GenerateAccessToken(user),
            refreshToken,
            DateTimeOffset.UtcNow.AddMinutes(tokenService.AccessTokenLifetimeMinutes),
            ToUserInfo(user));
    }

    private static UserInfo ToUserInfo(User user) =>
        new(
            user.Id,
            user.FullName,
            user.Email,
            user.Phone,
            user.TenantId,
            user.Tenant?.Name ?? string.Empty,
            user.Role?.Code ?? RoleCode.Cashier.ToString(),
            user.Role?.Name ?? string.Empty);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private async Task EnsureEmailAvailableAsync(string email, CancellationToken cancellationToken)
    {
        var exists = await context.Set<User>()
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == email, cancellationToken);

        if (exists)
            throw new ConflictException("Email này đã được đăng ký.", "email_already_exists");
    }

    private async Task EnsureRolesExistAsync(CancellationToken cancellationToken)
    {
        var codes = await context.Set<Role>()
            .Select(r => r.Code)
            .ToListAsync(cancellationToken);

        var missing = DefaultRoles
            .Where(r => !codes.Contains(r.Code.ToString()))
            .ToList();

        if (missing.Count == 0)
            return;

        context.Set<Role>().AddRange(missing.Select(r => new Role
        {
            Code = r.Code.ToString(),
            Name = r.Name,
            Description = r.Description,
        }));

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Role> GetRoleAsync(RoleCode code, CancellationToken cancellationToken)
    {
        var role = await context.Set<Role>()
            .FirstOrDefaultAsync(r => r.Code == code.ToString(), cancellationToken);

        return role ?? throw new InvalidOperationException($"Thiếu role {code}. Hãy chạy seeder dữ liệu.");
    }

    /// <summary>Tạo slug duy nhất từ tên cửa hàng (phục vụ subdomain / vanity URL).</summary>
    private async Task<string> GenerateSlugAsync(string shopName, CancellationToken cancellationToken)
    {
        var slug = Slugify(shopName);

        if (string.IsNullOrEmpty(slug))
            slug = $"shop-{Guid.NewGuid().ToString("N")[..8]}";

        var candidate = slug;
        var suffix = 2;

        while (await context.Set<Tenant>()
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Slug == candidate, cancellationToken))
        {
            candidate = $"{slug}-{suffix++}";
        }

        return candidate;
    }

    private static string Slugify(string input)
    {
        var normalized = input.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        }

        return string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}