using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Tryum.Application.Common.Interfaces;
using Tryum.Domain.Users;
using Tryum.Infrastructure.Common;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// JWT access token + refresh token management.
/// Secret key & expiry được config từ appsettings.json (Jwt: section).
/// </summary>
public class TokenService : ITokenService
{
    private readonly IApplicationDbContext _context;
    private readonly JwtOptions _jwt;

    public TokenService(IApplicationDbContext context, JwtOptions jwt)
    {
        _context = context;
        _jwt = jwt;
    }

    // ── ITokenService ───────────────────────────────────────────────────────
    public int AccessTokenLifetimeMinutes => _jwt.ExpiresInMinutes;

    public string GenerateAccessToken(User user)
    {
        var claims = user.GetClaims();
        var key = new SymmetricSecurityKey(Convert.FromBase64String(_jwt.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_jwt.ExpiresInMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<RefreshToken> GenerateRefreshTokenAsync(
        Guid userId,
        Guid tenantId,
        string? createdByIp,
        CancellationToken cancellationToken = default)
    {
        var refreshToken = CreateToken(userId, tenantId, createdByIp);

        _context.Set<RefreshToken>().Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return refreshToken;
    }

    public async Task<RefreshToken?> GetActiveRefreshTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        // Token chưa gắn tenant trong context lúc refresh => bỏ query filter.
        // Không dùng AsNoTracking: token cũ cần được track để persist IsRevoked khi rotate.
        return await _context.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Include(rt => rt.User).ThenInclude(u => u.Role)
            .Include(rt => rt.User).ThenInclude(u => u.Tenant)
            .FirstOrDefaultAsync(rt => rt.Token == token && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow, cancellationToken);
    }

    public async Task<RefreshToken> RotateRefreshTokenAsync(
        RefreshToken oldToken,
        string? createdByIp,
        CancellationToken cancellationToken = default)
    {
        oldToken.IsRevoked = true;
        oldToken.CreatedByIp = createdByIp;

        var newToken = CreateToken(oldToken.UserId, oldToken.TenantId, createdByIp);

        _context.Set<RefreshToken>().Add(newToken);
        await _context.SaveChangesAsync(cancellationToken);

        return newToken;
    }

    public async Task RevokeAllRefreshTokensAsync(
        Guid userId,
        string? createdByIp,
        CancellationToken cancellationToken = default)
    {
        // userId lấy từ token đã xác thực nên không cần (và không nên) phụ thuộc query filter.
        var tokens = await _context.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.CreatedByIp = createdByIp;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private RefreshToken CreateToken(Guid userId, Guid tenantId, string? createdByIp) => new()
    {
        Token = GenerateRandomToken(),
        ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpiresInDays),
        CreatedByIp = createdByIp,
        UserId = userId,
        TenantId = tenantId,
    };

    private static string GenerateRandomToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}