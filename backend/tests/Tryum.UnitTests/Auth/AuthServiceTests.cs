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
/// Test nghiệp vụ auth: đăng ký cửa hàng, đăng nhập, refresh token (rotation), đăng xuất.
/// Dùng EF Core InMemory (không cần Postgres) – logic nghiệp vụ không phụ thuộc RDBMS.
/// </summary>
public class AuthServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuthService _authService;
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

        _authService = new AuthService(_context, new PasswordHasher(), tokenService);
    }

    public void Dispose()
    {
        _context.Dispose();
        TenantContext.Current = null;
        GC.SuppressFinalize(this);
    }

    private static RegisterRequest Shop(string name = "Phở Thảo", string email = "owner@phothao.vn") =>
        new(ShopName: name, OwnerName: "Nguyễn Văn A", Email: email, Password: "Matkhau@123", Phone: "0901234567");

    [Fact]
    public async Task RegisterAsync_ShouldCreateTenantOwnerAndReturnTokens()
    {
        var result = await _authService.RegisterAsync(Shop(), ip: "127.0.0.1");

        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
        Assert.Equal("Chủ cửa hàng", result.User.RoleName);
        Assert.Equal("Phở Thảo", result.User.TenantName);

        var tenant = await _context.Tenants.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("pho-thao", tenant.Slug);

        var user = await _context.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(tenant.Id, user.TenantId);
        Assert.Equal("Owner", user.Role?.Code);
        Assert.NotEqual("Matkhau@123", user.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_ShouldRejectDuplicateEmail()
    {
        await _authService.RegisterAsync(Shop());

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _authService.RegisterAsync(Shop(name: "Quán Khác", email: "OWNER@phothao.vn")));

        Assert.Equal("email_already_exists", ex.ErrorCode);
    }

    [Fact]
    public async Task RegisterAsync_ShouldGenerateUniqueSlug()
    {
        await _authService.RegisterAsync(Shop(name: "Phở Thảo", email: "a@phothao.vn"));
        await _authService.RegisterAsync(Shop(name: "Phở Thảo", email: "b@phothao.vn"));

        var slugs = await _context.Tenants.IgnoreQueryFilters().Select(t => t.Slug).ToListAsync();
        Assert.Equal(["pho-thao", "pho-thao-2"], slugs.OrderBy(s => s));
        Assert.Equal(2, await _context.Users.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnTokens_WhenCredentialsValid()
    {
        await _authService.RegisterAsync(Shop());

        var result = await _authService.LoginAsync(
            new LoginRequest("OWNER@phothao.vn", "Matkhau@123"), ip: "127.0.0.1");

        Assert.NotEmpty(result.AccessToken);
        Assert.Equal("owner@phothao.vn", result.User.Email);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowUnauthorized_WhenPasswordWrong()
    {
        await _authService.RegisterAsync(Shop());

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _authService.LoginAsync(new LoginRequest("owner@phothao.vn", "sai-mat-khau")));
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowForbidden_WhenUserLocked()
    {
        await _authService.RegisterAsync(Shop());

        var user = await _context.Users.IgnoreQueryFilters().SingleAsync();
        user.IsActive = false;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _authService.LoginAsync(new LoginRequest("owner@phothao.vn", "Matkhau@123")));
    }

    [Fact]
    public async Task RefreshAsync_ShouldRotateToken_AndRevokeOldOne()
    {
        var registered = await _authService.RegisterAsync(Shop());

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
        var registered = await _authService.RegisterAsync(Shop());

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
        var registered = await _authService.RegisterAsync(Shop());

        using var scope = TenantContext.Use(registered.User.TenantId);
        var info = await _authService.GetCurrentUserAsync(registered.User.Id);

        Assert.Equal(registered.User.TenantId, info.TenantId);
        Assert.Equal("Owner", info.RoleCode);
    }
}