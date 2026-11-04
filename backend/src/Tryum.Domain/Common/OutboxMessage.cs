namespace Tryum.Domain.Common;

/// <summary>
/// Outbox pattern: lưu event cần publish vào DB cùng transaction với nghiệp vụ
/// (vd: tạo hóa đơn + ghi event order.created trong 1 commit). Một relay nền đọc
/// các bản ghi chưa publish, gửi lên RabbitMQ rồi đánh dấu PublishedAt.
/// Tránh mất event khi broker sập đúng lúc tạo dữ liệu.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Loại event, vd "order.created".</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Routing key khi publish lên exchange.</summary>
    public string RoutingKey { get; set; } = string.Empty;

    /// <summary>Payload dạng JSON (đã serialize sẵn).</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm đã publish thành công; null = chưa publish.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public int RetryCount { get; set; }

    public string? Error { get; set; }
}
