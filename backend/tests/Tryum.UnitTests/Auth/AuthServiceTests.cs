using Microsoft.EntityFrameworkCore;
using Tryum.Application.Auth;
using Tryum.Application.Common.Exceptions;
using Tryum.Application.Common.Interfaces;
using Tryum.Domain.Common;
using Tryum.Domain.Users;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;
using Tryum.Infrastructure.Services;

namespace Tryum.UnitTests.Auth;

/// <summary>
/// Test nghiệp vụ auth: đăng ký cửa hàng (kèm OTP), đăng nhập, refresh token (rotation), đăng xuất.
/// Dùng EF Core InMemory (không cần Postgres) – logic nghiệp vụ không phụ thuộc RDBMS.
/// </summary>
public class AuthServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuthService _authService;
    private readonly OtpService _otpService = new();
    private readonly JwtOptions _jwt = new()
    {
        SecretKey = Convert.ToBase64String(
            Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
        Issuer = "tryum-test",
        Audience = "tryum-test-web",
        ExpiresInMinutes = 60,
        RefreshTokenExpiresInDays = 30,
    };

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);

        var tokenService = new TokenService(_context, _jwt);

        _authService = new AuthService(_context, new PasswordHasher(), tokenService, _otpService);
    }

    public void Dispose()
    {
        _context.Dispose();
        TenantContext.Current = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sản xuất RegisterRequest kèm mã OTP hợp lệ.
    /// Mỗi lần gọi dùng một SĐT khác nhau vì OtpService giới hạn gửi lại trong 30 giây.
    /// </summary>
    private async Task<RegisterRequest> ShopAsync(
        string name = "Phở Thảo",
        string email = "owner@phothao.vn",
        string phone = "0901234567")
    {
        var result = await _otpService.IssueAsync(phone);
        return new RegisterRequest(name, "Nguyễn Văn A", email, "Matkhau@123", phone, result.Code);
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreateTenantOwnerAndReturnTokens()
    {
        var result = await _authService.RegisterAsync(await ShopAsync(), ip: "127.0.0.1");

        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
        Assert.Equal("Chủ cửa hàng", result.User.RoleName);
        Assert.Equal("Phở Thảo", result.User.TenantName);

        var tenant = await _context.Tenants.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("pho-thao", tenant.Slug);
        Assert.Equal("+84901234567", tenant.Phone);

        var user = await _context.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(tenant.Id, user.TenantId);
        Assert.Equal("Owner", user.Role?.Code);
        Assert.NotEqual("Matkhau@123", user.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_ShouldRejectWrongOtp()
    {
        await _otpService.IssueAsync("0901234567");

        var ex = await Assert.ThrowsAsync<ValidationException>(
            async () => await _authService.RegisterAsync(new RegisterRequest(
                "Phở Thảo", "Nguyễn Văn A", "owner@phothao.vn", "Matkhau@123", "0901234567", "000000")));

        Assert.Equal("otp_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task RegisterAsync_ShouldRejectWithoutOtp()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            async () => await _authService.RegisterAsync(new RegisterRequest(
                "Phở Thảo", "Nguyễn Văn A", "owner@phothao.vn", "Matkhau@123", "0901234567", "123456")));

        Assert.Equal("otp_expired", ex.ErrorCode);
    }

    [Fact]
    public async Task RegisterAsync_ShouldRejectDuplicateEmail()
    {
        await _authService.RegisterAsync(await ShopAsync());

        var ex = await Assert.ThrowsAsync<ConflictException>(
            async () => await _authService.RegisterAsync(await ShopAsync(
                name: "Quán Khác", email: "OWNER@phothao.vn", phone: "0909999999")));

        Assert.Equal("email_already_exists", ex.ErrorCode);
    }

    [Fact]
    public async Task RegisterAsync_ShouldRejectDuplicatePhone()
    {
        await _authService.RegisterAsync(await ShopAsync(phone: "0355951359"));

        // SĐT đã có tài khoản => chặn ngay, không tạo tenant thứ hai cho cùng 1 số.
        // Dùng service không cooldown vì OTP cho số này vừa được phát ở bước trên.
        var otpService = new OtpService(
            new LogSmsSender(), options: new OtpOptions { CooldownSeconds = 0 });

        var second = await otpService.IssueAsync("0355951359");
        var ex = await Assert.ThrowsAsync<ConflictException>(
            async () => await _authService.RegisterAsync(new RegisterRequest(
                "Quán Khác", "Nguyễn Văn B", "khac@phothao.vn", "Matkhau@123",
                "0355951359", second.Code)));

        Assert.Equal("phone_already_registered", ex.ErrorCode);
        Assert.Equal(1, await _context.Tenants.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task EnsurePhoneAvailableAsync_ShouldThrow_WhenPhoneRegistered()
    {
        await _authService.RegisterAsync(await ShopAsync(phone: "0355951359"));

        await Assert.ThrowsAsync<ConflictException>(
            async () => await _authService.EnsurePhoneAvailableAsync("0355951359"));
    }

    [Fact]
    public async Task EnsurePhoneAvailableAsync_ShouldPass_ForNewPhone()
    {
        await _authService.EnsurePhoneAvailableAsync("0901234567");
    }

    [Fact]
    public async Task LoginAsync_ShouldAcceptPhoneNumber()
    {
        await _authService.RegisterAsync(await ShopAsync(phone: "0355951359"));

        // KiotViet đăng nhập bằng SĐT – Tryum hỗ trợ cả SĐT lẫn email.
        var byPhone = await _authService.LoginAsync(
            new LoginRequest("0355951359", "Matkhau@123"), ip: "127.0.0.1");

        var byE164 = await _authService.LoginAsync(
            new LoginRequest("+84355951359", "Matkhau@123"), ip: "127.0.0.1");

        Assert.Equal(byPhone.User.Id, byE164.User.Id);
        Assert.Equal("+84355951359", byPhone.User.Phone);
    }

    [Fact]
    public async Task RegisterAsync_ShouldGenerateUniqueSlug()
    {
        await _authService.RegisterAsync(await ShopAsync(name: "Phở Thảo", email: "a@phothao.vn", phone: "0901111111"));
        await _authService.RegisterAsync(await ShopAsync(name: "Phở Thảo", email: "b@phothao.vn", phone: "0902222222"));

        var slugs = await _context.Tenants.IgnoreQueryFilters().Select(t => t.Slug).ToListAsync();
        Assert.Equal(["pho-thao", "pho-thao-2"], slugs.OrderBy(s => s));
        Assert.Equal(2, await _context.Users.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnTokens_WhenCredentialsValid()
    {
        await _authService.RegisterAsync(await ShopAsync());

        var result = await _authService.LoginAsync(
            new LoginRequest("OWNER@phothao.vn", "Matkhau@123"), ip: "127.0.0.1");

        Assert.NotEmpty(result.AccessToken);
        Assert.Equal("owner@phothao.vn", result.User.Email);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowUnauthorized_WhenPasswordWrong()
    {
        await _authService.RegisterAsync(await ShopAsync());

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _authService.LoginAsync(new LoginRequest("owner@phothao.vn", "sai-mat-khau")));
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowForbidden_WhenUserLocked()
    {
        await _authService.RegisterAsync(await ShopAsync());

        var user = await _context.Users.IgnoreQueryFilters().SingleAsync();
        user.IsActive = false;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _authService.LoginAsync(new LoginRequest("owner@phothao.vn", "Matkhau@123")));
    }

    [Fact]
    public async Task RefreshAsync_ShouldRotateToken_AndRevokeOldOne()
    {
        var registered = await _authService.RegisterAsync(await ShopAsync());

        var refreshed = await _authService.RefreshAsync(
            new RefreshRequest(registered.RefreshToken), ip: "127.0.0.1");

        Assert.NotEqual(registered.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(registered.User.Id, refreshed.User.Id);

        // Token cũ đã bị revoke => không dùng lại được (token reuse detection).
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _authService.RefreshAsync(new RefreshRequest(registered.RefreshToken)));
    }

    [Fact]
    public async Task LogoutAsync_ShouldRevokeAllRefreshTokens()
    {
        var registered = await _authService.RegisterAsync(await ShopAsync());

        await _authService.LogoutAsync(registered.User.Id);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _authService.RefreshAsync(new RefreshRequest(registered.RefreshToken)));
    }

    [Fact]
    public async Task GetCurrentUserAsync_ShouldThrowNotFound_ForUnknownUser()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _authService.GetCurrentUserAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetCurrentUserAsync_ShouldReturnTenantAndRole()
    {
        var registered = await _authService.RegisterAsync(await ShopAsync());

        using var scope = TenantContext.Use(registered.User.TenantId);
        var info = await _authService.GetCurrentUserAsync(registered.User.Id);

        Assert.Equal(registered.User.TenantId, info.TenantId);
        Assert.Equal("Owner", info.RoleCode);
    }
}
