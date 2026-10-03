using Tryum.Domain.Users;
using Tryum.Application.Common.Security;

namespace Tryum.Api.Common;

/// <summary>Cấu hình policy-based authorization theo vai trò (RoleCode).</summary>
public static class AuthorizationSetup
{
    public static IServiceCollection AddTryumAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Authenticated, policy =>
                policy.RequireAuthenticatedUser())

            .AddPolicy(AuthorizationPolicies.OwnerOrManager, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole(RoleCode.Owner.ToString(), RoleCode.Manager.ToString()));

        return services;
    }
}