using Sentry;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Observability;

/// <summary>
/// Gắn <c>tenant_id</c> vào mọi event Sentry để lọc lỗi theo từng cửa hàng.
/// Chạy trên mọi event, đọc tenant từ <see cref="TenantContext"/> (AsyncLocal của request).
/// </summary>
public sealed class TenantSentryProcessor : ISentryEventProcessor
{
    public SentryEvent Process(SentryEvent @event)
    {
        if (TenantContext.Current is { } tenantId)
            @event.SetTag("tenant_id", tenantId.ToString());

        return @event;
    }
}
