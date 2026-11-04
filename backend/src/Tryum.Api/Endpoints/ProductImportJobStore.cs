using System.Collections.Concurrent;
using Tryum.Application.Common.Interfaces;

namespace Tryum.Api.Endpoints;

/// <summary>Tiến trình import sản phẩm chạy nền để UI poll được cả sản phẩm lẫn ảnh.</summary>
public sealed class ProductImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = "Reading";
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int ImagesQueued { get; set; }
    public bool ImageStorageConfigured { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public long ElapsedMs { get; set; }

    public object Snapshot() => new
    {
        id = Id,
        status = Status,
        fileName = FileName,
        total = Total,
        processed = Processed,
        created = Created,
        updated = Updated,
        imagesQueued = ImagesQueued,
        imageStorageConfigured = ImageStorageConfigured,
        error = Error,
        startedAt = StartedAt,
        finishedAt = FinishedAt,
        elapsedMs = ElapsedMs,
    };
}

/// <summary>
/// Lưu tiến trình import: ưu tiên Redis dùng chung để nhiều instance API poll được;
/// vẫn giữ bản in-memory trên instance đang chạy để loop import không phải đọc Redis từng dòng.
/// </summary>
public sealed class ProductImportJobStore
{
    private readonly ConcurrentDictionary<Guid, ProductImportJob> _jobs = new();
    private readonly IRedisStore? _redis;

    public ProductImportJobStore(IRedisStore? redis = null)
    {
        _redis = redis is { IsConfigured: true } ? redis : null;
    }

    public ProductImportJob Create(Guid tenantId, string fileName)
    {
        Cleanup();
        var job = new ProductImportJob { TenantId = tenantId, FileName = fileName };
        _jobs[job.Id] = job;
        _ = PersistAsync(job);
        return job;
    }

    /// <summary>Lấy tiến trình: ưu tiên bản in-memory (tươi nhất trên instance đang import), sau đó Redis.</summary>
    public async Task<ProductImportJob?> GetAsync(Guid id)
    {
        if (_jobs.TryGetValue(id, out var mem))
            return mem;

        if (_redis is not null)
            return await _redis.GetAsync<ProductImportJob>($"import:{id}");

        return null;
    }

    /// <summary>Ghi lại snapshot vào Redis (best-effort) – gọi ở các mốc quan trọng của loop import.</summary>
    public Task SaveAsync(ProductImportJob job)
    {
        _jobs[job.Id] = job;
        return PersistAsync(job);
    }

    private Task PersistAsync(ProductImportJob job)
    {
        if (_redis is null) return Task.CompletedTask;
        return _redis.SetAsync($"import:{job.Id}", job, TimeSpan.FromHours(6));
    }

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-2);
        foreach (var pair in _jobs)
        {
            if (pair.Value.FinishedAt is { } finishedAt && finishedAt < cutoff)
                _jobs.TryRemove(pair.Key, out _);
            else if (pair.Value.StartedAt < DateTimeOffset.UtcNow.AddHours(-6))
                _jobs.TryRemove(pair.Key, out _);
        }
    }
}
