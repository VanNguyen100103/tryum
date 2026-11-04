using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tryum.Api.Messaging;

/// <summary>
/// Publisher tối giản cho event bus. Nếu RabbitMQ chưa cấu hình hoặc lỗi mạng,
/// API vẫn chạy bình thường và chỉ ghi log cảnh báo.
/// </summary>
public sealed class RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

    public async Task<bool> PublishAsync(string routingKey, object payload, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        try
        {
            var channel = await GetChannelAsync(ct);
            if (channel is null) return false;

            var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = Guid.NewGuid().ToString("N"),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            };

            await _publishLock.WaitAsync(ct);
            try
            {
                await channel.BasicPublishAsync(
                    exchange: _options.Exchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: ct);
            }
            finally
            {
                _publishLock.Release();
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không publish được event {RoutingKey} lên RabbitMQ", routingKey);
            return false;
        }
    }

    private async Task<IChannel?> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true }) return _channel;

            if (_connection is null || !_connection.IsOpen)
            {
                var factory = new ConnectionFactory
                {
                    HostName = _options.Host,
                    Port = _options.Port,
                    UserName = _options.Username,
                    Password = _options.Password,
                    VirtualHost = string.IsNullOrWhiteSpace(_options.VirtualHost) ? "/" : _options.VirtualHost,
                    AutomaticRecoveryEnabled = true,
                    Ssl = new SslOption
                    {
                        Enabled = _options.Ssl,
                        ServerName = _options.Host,
                    },
                };
                _connection = await factory.CreateConnectionAsync("tryum-api", ct);
                logger.LogInformation("Đã kết nối RabbitMQ tại {Host}:{Port}", _options.Host, _options.Port);
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
            await _channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: ct);
            // Đảm bảo queue + binding tồn tại để event không bị vứt khi chưa có consumer.
            await _channel.QueueDeclareAsync(
                queue: OrderEvents.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: ct);
            await _channel.QueueBindAsync(
                queue: OrderEvents.QueueName,
                exchange: _options.Exchange,
                routingKey: OrderEvents.RoutingKeyOrderCreated,
                cancellationToken: ct);
            return _channel;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không kết nối được RabbitMQ, event sẽ bị bỏ qua");
            return null;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
