using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Tryum.Application.Auth;
using Tryum.Application.Common.Interfaces;

namespace Tryum.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký tất cả service của Application layer vào DI container.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // ── Auth (Phase 2) ──────────────────────────────────────────────────
        services.AddScoped<IAuthService, AuthService>();

        // ── Validation (FluentValidation) ───────────────────────────────────
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        // MediatR sẽ được đăng ký khi các phase sau dùng tới (command/query).
        return services;
    }
}