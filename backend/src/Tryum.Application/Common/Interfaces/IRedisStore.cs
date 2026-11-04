namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Store key-value dùng chung Redis (Upstash) cho nhiều instance API.
/// Tất cả key đều tự thêm prefix "tryum:" để không đụng dự án khác.
/// Khi Redis chưa cấu hình → trả default/không lỗi (caller tự quyết định fallback).
/// </summary>
public interface IRedisStore
{
    bool IsConfigured { get; }

    /// <summary>Đọc giá trị đã lưu (deserialize JSON); không có/Redis lỗi → default.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Ghi giá trị (serialize JSON), tuỳ chọn TTL.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>Đọc + xoá nguyên tử (GETDEL) – dùng cho mã chỉ dùng một lần.</summary>
    Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Xoá một key; trả true nếu key tồn tại.</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Tăng counter; trả giá trị sau khi tăng.</summary>
    Task<long> IncrementAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Đặt lại TTL cho key đã tồn tại.</summary>
    Task SetExpiryAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Cache pattern: đọc cache, miss thì chạy factory rồi ghi cache.</summary>
    Task<T> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory, CancellationToken cancellationToken = default);
}
