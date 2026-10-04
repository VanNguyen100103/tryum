using FluentValidation;
using Tryum.Application.Auth;
using Tryum.Application.Common.Exceptions;
using Tryum.Application.Common.Interfaces;
using Tryum.Application.Common.Security;

namespace Tryum.Api.Endpoints;

/// <summary>
/// Endpoint xác thực: /api/v1/auth/*
/// Public: register, login, refresh. Cần token: logout, me.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth");

        group.MapGet("/captcha", (ICaptchaService captchaService) =>
            {
                var challenge = captchaService.Create();

                return Results.Ok(new CaptchaResponse(challenge.CaptchaId, challenge.ImageDataUrl));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Sinh ảnh captcha (server-side) cho form đăng ký")
            .Produces<CaptchaResponse>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/otp/send", async (
                SendOtpRequest request,
                IOtpService otpService,
                IAuthService authService,
                ICaptchaService captchaService,
                IValidator<SendOtpRequest> validator,
                IConfiguration configuration,
                CancellationToken ct) =>
            {
                await ValidateAsync(validator, request, ct);

                // 1) Kiểm captcha SERVER-SIDE trước: nếu không, bot gọi thẳng API sẽ đốt quota SMS.
                // Lần gửi đầu bắt buộc captcha; lần "gửi lại" ở bước OTP thì không (đã bị cooldown chặn).
                var captchaRequired = !request.IsResend && !string.Equals(
                    configuration["Captcha:Required"],
                    "false",
                    StringComparison.OrdinalIgnoreCase);

                if (captchaRequired && !captchaService.Validate(request.CaptchaId, request.CaptchaCode))
                    throw new Tryum.Application.Common.Exceptions.ValidationException(
                        "Mã xác thực không đúng hoặc đã hết hạn. Vui lòng tải lại ảnh và thử lại.",
                        "captcha_invalid");

                // 2) SĐT đã có tài khoản thì báo luôn, không tốn 1 tin SMS.
                await authService.EnsurePhoneAvailableAsync(request.Phone, ct);

                // 3) Mới phát tin SMS (OtpService tự kiểm cooldown + giới hạn/ngày).
                var result = await otpService.IssueAsync(request.Phone, ct);

                // Demo mode (mô phỏng SMS, 0đ): trả về mã để UI hiển thị.
                // Production (cắm SMS gateway thật): xoá dòng này / tắt flag.
                var demoOtp = string.Equals(
                        configuration["Auth:DemoOtpInResponse"],
                        "true",
                        StringComparison.OrdinalIgnoreCase)
                    ? result.Code
                    : null;

                return Results.Ok(new SendOtpResponse(
                    Sent: true,
                    result.ResendAfterSeconds,
                    demoOtp,
                    result.ExpiresInSeconds));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Gửi mã OTP tới SĐT (yêu cầu captcha hợp lệ; demo: trả mã thay vì gọi SMS gateway)")
            .Produces<SendOtpResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/register", async (
                RegisterRequest request,
                IAuthService authService,
                IValidator<RegisterRequest> validator,
                HttpContext http,
                CancellationToken ct) =>
            {
                await ValidateAsync(validator, request, ct);

                var result = await authService.RegisterAsync(
                    request,
                    http.Connection.RemoteIpAddress?.ToString(),
                    ct);

                return Results.Created($"/api/v1/auth/me", result);
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Đăng ký cửa hàng mới (tạo tenant + tài khoản chủ)")
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (
                LoginRequest request,
                IAuthService authService,
                IValidator<LoginRequest> validator,
                HttpContext http,
                CancellationToken ct) =>
            {
                await ValidateAsync(validator, request, ct);

                var result = await authService.LoginAsync(
                    request,
                    http.Connection.RemoteIpAddress?.ToString(),
                    ct);

                return Results.Ok(result);
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Đăng nhập, trả về access token + refresh token")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/refresh", async (
                RefreshRequest request,
                IAuthService authService,
                IValidator<RefreshRequest> validator,
                HttpContext http,
                CancellationToken ct) =>
            {
                await ValidateAsync(validator, request, ct);

                var result = await authService.RefreshAsync(
                    request,
                    http.Connection.RemoteIpAddress?.ToString(),
                    ct);

                return Results.Ok(result);
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Đổi refresh token lấy access token mới (rotation)")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (
                IAuthService authService,
                ICurrentUserService currentUser,
                CancellationToken ct) =>
            {
                await authService.LogoutAsync(currentUser.RequireUserId(), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(AuthorizationPolicies.Authenticated)
            .WithSummary("Đăng xuất – hủy toàn bộ refresh token của user")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", async (
                IAuthService authService,
                ICurrentUserService currentUser,
                CancellationToken ct) =>
            {
                var user = await authService.GetCurrentUserAsync(currentUser.RequireUserId(), ct);
                return Results.Ok(user);
            })
            .RequireAuthorization(AuthorizationPolicies.Authenticated)
            .WithSummary("Thông tin user hiện tại")
            .Produces<UserInfo>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task ValidateAsync<T>(IValidator<T> validator, T request, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(request, ct);

        if (result.IsValid)
            return;

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        throw new RequestValidationException(errors);
    }
}