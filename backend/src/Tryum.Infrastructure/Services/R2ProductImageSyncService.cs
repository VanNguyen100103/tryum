using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Interfaces;
using Tryum.Domain.Products;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Infrastructure.Services;

public sealed class R2ProductImageSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<R2Options> options,
    ILogger<R2ProductImageSyncService> logger) : BackgroundService
{
    private const int BatchSize = 8;
    private const long MaxImageBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> AllowedImageHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdn2-retail-images.kiotviet.vn",
        "cdn1-retail-images.kiotviet.vn",
        "cdn-images.kiotviet.vn",
        "han02.vstorage.vngcloud.vn",
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!options.Value.IsConfigured)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                continue;
            }

            try
            {
                var batch = await LoadPendingAsync(stoppingToken);
                if (batch.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                await Parallel.ForEachAsync(batch, new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = stoppingToken,
                }, SyncOneAsync);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Lỗi khi đồng bộ ảnh sản phẩm lên R2.");
                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            }
        }
    }

    private async Task<List<PendingImage>> LoadPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ProductImages.IgnoreQueryFilters()
            .Where(image => image.Status == "Pending")
            .OrderBy(image => image.CreatedAt)
            .Select(image => new PendingImage(image.Id, image.TenantId, image.SourceUrl, image.ObjectKey))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
    }

    private async ValueTask SyncOneAsync(PendingImage image, CancellationToken cancellationToken)
    {
        string? error = null;
        var contentType = "application/octet-stream";
        var status = "Failed";
        try
        {
            if (!Uri.TryCreate(image.SourceUrl, UriKind.Absolute, out var source)
                || source.Scheme != Uri.UriSchemeHttps
                || !AllowedImageHosts.Contains(source.Host))
                throw new InvalidOperationException("Ảnh không nằm trên CDN KiotViet được cho phép.");

            await using var scope = scopeFactory.CreateAsyncScope();
            var clientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            var client = clientFactory.CreateClient("product-images");
            using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Nguồn không trả về tệp hình ảnh.");
            if (response.Content.Headers.ContentLength is > MaxImageBytes)
                throw new InvalidOperationException("Ảnh vượt quá giới hạn 8 MB.");

            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var imageBuffer = new MemoryStream();
            var block = new byte[64 * 1024];
            int read;
            while ((read = await sourceStream.ReadAsync(block, cancellationToken)) > 0)
            {
                if (imageBuffer.Length + read > MaxImageBytes)
                    throw new InvalidOperationException("Ảnh vượt quá giới hạn 8 MB.");
                await imageBuffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
            }
            imageBuffer.Position = 0;
            await storage.PutAsync(image.ObjectKey, imageBuffer, contentType, cancellationToken);
            status = "Stored";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            error = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            logger.LogWarning(exception, "Không đồng bộ được ảnh sản phẩm {ProductImageId}.", image.Id);
        }

        await using var updateScope = scopeFactory.CreateAsyncScope();
        var updateDb = updateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = await updateDb.ProductImages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.Id == image.Id && row.TenantId == image.TenantId, cancellationToken);
        if (record is null) return;
        record.Status = status;
        record.LastError = error;
        if (status == "Stored") record.ContentType = contentType;
        if (status == "Stored")
        {
            var product = await updateDb.Products.IgnoreQueryFilters()
                .SingleOrDefaultAsync(row => row.Id == record.ProductId && row.TenantId == record.TenantId, cancellationToken);
            if (product is not null)
            {
                var keys = await updateDb.ProductImages.IgnoreQueryFilters()
                    .Where(row => row.ProductId == record.ProductId && row.Status == "Stored")
                    .Select(row => row.ObjectKey)
                    .ToListAsync(cancellationToken);
                if (!keys.Contains(record.ObjectKey, StringComparer.Ordinal)) keys.Add(record.ObjectKey);
                product.R2ObjectKeysJson = JsonSerializer.Serialize(keys);
            }
        }
        await updateDb.SaveChangesAsync(cancellationToken);
    }

    private sealed record PendingImage(Guid Id, Guid TenantId, string SourceUrl, string ObjectKey);
}
