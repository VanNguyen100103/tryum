using Microsoft.Extensions.Logging;
using Tryum.Application.Common.Interfaces;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// Gửi SMS giả lập cho môi trường dev/demo (0đ): chỉ ghi nội dung tin vào log, không gọi mạng.
/// Production cắm <see cref="SmsGateSmsSender"/> (self-host, miễn phí) hoặc gateway SMS thật.
/// </summary>
public sealed class LogSmsSender(ILogger<LogSmsSender>? logger = null) : ISmsSender
{
    public Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("SMS demo (0đ) tới {Phone}: {Message}", phoneE164, message);
        return Task.CompletedTask;
    }
}
