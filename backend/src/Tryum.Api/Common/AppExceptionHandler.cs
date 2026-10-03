using Microsoft.AspNetCore.Diagnostics;
using Tryum.Application.Common.Exceptions;

namespace Tryum.Api.Common;

/// <summary>
/// Biến <see cref="AppException"/> thành ProblemDetails (RFC 9457) với đúng status code,
/// các lỗi khác trả 500 và không lộ chi tiết cho client.
/// </summary>
public sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not AppException appException)
        {
            logger.LogError(exception, "Lỗi không xử lý được: {Message}", exception.Message);
            return false; // để middleware mặc định xử lý (500)
        }

        logger.LogWarning(
            "Nghiệp vụ trả lỗi {StatusCode}: {Message}",
            (int)appException.StatusCode,
            appException.Message);

        httpContext.Response.StatusCode = (int)appException.StatusCode;

        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = appException.ErrorCode,
            ["traceId"] = httpContext.TraceIdentifier,
        };

        if (appException is RequestValidationException validationException)
            extensions["errors"] = validationException.Errors;

        await Results.Problem(
            statusCode: (int)appException.StatusCode,
            title: appException.StatusCode.ToString(),
            detail: appException.Message,
            extensions: extensions)
            .ExecuteAsync(httpContext);

        return true;
    }
}