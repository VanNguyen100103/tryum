using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Tryum.Api.Messaging;

/// <summary>
/// Consumer đơn hàng: lắng nghe queue <c>tryum.order.events</c> (competing consumers —
/// nhiều instance cùng nghe, RabbitMQ tự chia việc). Hiện chỉ log payload + ack;
/// chỗ này là nơi cắm nghiệp vụ thật (thông báo, đồng bộ, email/telegram...).
/// </summary>
public sealed class OrderEventConsumer(IOptions<RabbitMqOptions> options, ILogger<OrderEventConsumer> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            logger.LogInformation("RabbitMQ chưa cấu hình, bỏ qua consumer.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var channel = await EnsureChannelAsync(stoppingToken);
                if (channel is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, stoppingToken);

                await channel.BasicConsumeAsync(
                    queue: OrderEvents.QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken);
                logger.LogInformation("Đang lắng nghe queue {Queue}", OrderEvents.QueueName);

                var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await using var registration = stoppingToken.Register(() => wait.TrySetResult());
                await wait.Task;
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Consumer RabbitMQ lỗi, thử lại sau 5 giây.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            logger.LogInformation(
                "Nhận event {RoutingKey} (MessageId={MessageId}): {Payload}",
                ea.RoutingKey, ea.BasicProperties?.MessageId, body);

            // TODO: cắm nghiệp vụ thật tại đây (thông báo/sync/email...).

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Xử lý event {RoutingKey} lỗi, requeue.", ea.RoutingKey);
            // requeue = giao lại cho consumer khác/lần sau. Nếu lỗi vĩnh viễn sẽ vòng lặp
            // (nên thêm x-retry/ttl sau); tạm chấp nhận ở mức hiện tại.
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
        }
    }

    private async Task<IChannel?> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        try
        {
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
                    Ssl = new SslOption { Enabled = _options.Ssl, ServerName = _options.Host },
                };
                _connection = await factory.CreateConnectionAsync("tryum-api-consumer", ct);
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
            await _channel.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
            await _channel.QueueDeclareAsync(OrderEvents.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
            await _channel.QueueBindAsync(OrderEvents.QueueName, _options.Exchange, OrderEvents.RoutingKeyOrderCreated, cancellationToken: ct);
            return _channel;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không kết nối được RabbitMQ cho consumer.");
            return null;
        }
    }
}
