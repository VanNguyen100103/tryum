using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Messaging;

/// <summary>
/// Relay outbox: đọc các OutboxMessage chưa publish, gửi lên RabbitMQ rồi đánh dấu PublishedAt.
/// Chỉ 1 instance chạy (leader lock Redis) để không publish trùng. At-least-once delivery.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    RabbitMqPublisher rabbit,
    IRedisStore redis,
    IOptions<RabbitMqOptions> rabbitOptions,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan LeaderTtl = TimeSpan.FromSeconds(60);
    private const string LeaderKey = "outbox:leader";
    private const int BatchSize = 50;
    private const int MaxRetry = 8;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(rabbitOptions.Value.Host))
        {
            logger.LogInformation("RabbitMQ chưa cấu hình, bỏ qua outbox publisher.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await IsLeaderAsync(stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }

                var hadMessages = await ProcessBatchAsync(stoppingToken);
                // Có việc thì xử lý liên tục; rảnh thì nghỉ lâu để không đốt quota Redis.
                await Task.Delay(hadMessages ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox publisher lỗi, thử lại sau 5 giây.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<bool> IsLeaderAsync(CancellationToken ct)
    {
        if (await redis.TryAcquireLockAsync(LeaderKey, _owner, LeaderTtl, ct))
            return true;

        if (await redis.GetStringAsync(LeaderKey, ct) == _owner)
        {
            await redis.SetExpiryAsync(LeaderKey, LeaderTtl, ct);
            return true;
        }

        return false;
    }

    private async Task<bool> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batch = await db.OutboxMessages
            .Where(message => message.PublishedAt == null && message.RetryCount < MaxRetry)
            .OrderBy(message => message.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0)
            return false;

        foreach (var message in batch)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var payload = JsonSerializer.Deserialize<JsonElement>(message.Payload);
                var ok = await rabbit.PublishAsync(message.RoutingKey, payload, ct);
                if (ok)
                {
                    message.PublishedAt = DateTimeOffset.UtcNow;
                    message.Error = null;
                }
                else
                {
                    message.RetryCount++;
                    message.Error = "Publish trả về false (broker không sẵn sàng).";
                }
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            }
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
