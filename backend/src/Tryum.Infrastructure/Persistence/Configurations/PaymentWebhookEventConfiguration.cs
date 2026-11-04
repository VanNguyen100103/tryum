using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Sales;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable("payment_webhook_events");
        builder.HasKey(webhookEvent => webhookEvent.Id);

        builder.Property(webhookEvent => webhookEvent.Provider).HasMaxLength(40).IsRequired();
        builder.Property(webhookEvent => webhookEvent.EventId).HasMaxLength(120).IsRequired();
        builder.Property(webhookEvent => webhookEvent.PayloadJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(webhookEvent => new { webhookEvent.Provider, webhookEvent.EventId })
            .IsUnique()
            .HasDatabaseName("ux_payment_webhook_events_provider_event");
    }
}
