namespace Tryum.Api.Messaging;

/// <summary>Tên queue + routing key cho event đơn hàng (order.created, ...).</summary>
public static class OrderEvents
{
    public const string QueueName = "tryum.order.events";
    public const string RoutingKeyOrderCreated = "order.created";
}
