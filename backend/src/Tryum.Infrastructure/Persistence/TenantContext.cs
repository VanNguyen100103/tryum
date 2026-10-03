namespace Tryum.Infrastructure.Persistence;

/// <summary>
/// Tenant hiện tại của request (multi-tenant).
/// Dùng <see cref="AsyncLocal{T}"/> nên giá trị chỉ nằm trong async flow của request đang chạy,
/// an toàn khi API xử lý nhiều request song song.
/// </summary>
public static class TenantContext
{
    private static readonly AsyncLocal<Guid?> Storage = new();

    /// <summary>
    /// TenantId hiện tại. Null = chưa xác định (request chưa đăng nhập, hoặc đang seed dữ liệu).
    /// </summary>
    public static Guid? Current
    {
        get => Storage.Value;
        set => Storage.Value = value;
    }

    /// <summary>TenantId hiện tại, ném lỗi nếu request chưa có tenant (thiếu/không hợp lệ token).</summary>
    public static Guid RequireCurrent() =>
        Current ?? throw new InvalidOperationException(
            "Không xác định được tenant hiện tại. Endpoint này yêu cầu request đã đăng nhập.");

    /// <summary>Tạm thời gán tenant (dùng trong test, seeder, background job).</summary>
    public static IDisposable Use(Guid? tenantId) => new Scope(tenantId);

    private sealed class Scope : IDisposable
    {
        private readonly Guid? _previous;

        public Scope(Guid? tenantId)
        {
            _previous = Current;
            Current = tenantId;
        }

        public void Dispose() => Current = _previous;
    }
}