using System.Net;

namespace Tryum.Application.Common.Exceptions;

/// <summary>
/// Lỗi nghiệp vụ có mã HTTP – Api layer chuyển thành ProblemDetails (RFC 9457).
/// </summary>
public class AppException : Exception
{
    public AppException(string message, HttpStatusCode statusCode, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode ?? statusCode.ToString();
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Mã lỗi ổn định để frontend xử lý (vd: "email_already_exists").</summary>
    public string ErrorCode { get; }
}

/// <summary>Dữ liệu không tồn tại (404).</summary>
public sealed class NotFoundException(string message)
    : AppException(message, HttpStatusCode.NotFound, "not_found");

/// <summary>Dữ liệu không hợp lệ (400).</summary>
public sealed class ValidationException(string message, string? errorCode = null)
    : AppException(message, HttpStatusCode.BadRequest, errorCode ?? "validation_error");

/// <summary>Sai email/mật khẩu, hoặc thiếu quyền (401).</summary>
public sealed class UnauthorizedException(string message = "Email hoặc mật khẩu không đúng.")
    : AppException(message, HttpStatusCode.Unauthorized, "unauthorized");

/// <summary>Đã đăng nhập nhưng không đủ quyền (403).</summary>
public sealed class ForbiddenException(string message = "Bạn không có quyền thực hiện thao tác này.")
    : AppException(message, HttpStatusCode.Forbidden, "forbidden");

/// <summary>Xung đột dữ liệu, ví dụ email đã tồn tại (409).</summary>
public sealed class ConflictException(string message, string errorCode = "conflict")
    : AppException(message, HttpStatusCode.Conflict, errorCode);

/// <summary>Quá tần suất, ví dụ gửi lại OTP trong thời gian cooldown (429).</summary>
public sealed class RateLimitedException(string message = "Bạn thao tác quá nhanh, vui lòng thử lại sau.", string errorCode = "rate_limited")
    : AppException(message, HttpStatusCode.TooManyRequests, errorCode);

/// <summary>
/// Dữ liệu request không hợp lệ theo validator (400) – kèm lỗi chi tiết theo từng field.
/// </summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("Dữ liệu gửi lên không hợp lệ.", HttpStatusCode.BadRequest, "validation_error")
{
    /// <summary>Lỗi chi tiết theo tên field để frontend hiển thị.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}