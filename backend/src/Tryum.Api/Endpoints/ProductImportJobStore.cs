using System.Collections.Concurrent;

namespace Tryum.Api.Endpoints;

/// <summary>Tiến trình import sản phẩm chạy nền để UI poll được cả sản phẩm lẫn ảnh.</summary>
public sealed class ProductImportJob
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Status { get; set; } = "Reading";
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int ImagesQueued { get; set; }
    public bool ImageStorageConfigured { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
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

public sealed class ProductImportJobStore
{
    private readonly ConcurrentDictionary<Guid, ProductImportJob> _jobs = new();

    public ProductImportJob Create(Guid tenantId, string fileName)
    {
        Cleanup();
        var job = new ProductImportJob { TenantId = tenantId, FileName = fileName };
        _jobs[job.Id] = job;
        return job;
    }

    public ProductImportJob? Get(Guid id) => _jobs.TryGetValue(id, out var job) ? job : null;

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
