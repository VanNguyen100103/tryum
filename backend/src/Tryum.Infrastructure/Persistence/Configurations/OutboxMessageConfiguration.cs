using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Common;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        // Bảng outbox được tạo riêng bằng raw SQL (OutboxSchema.EnsureCreatedAsync),
        // không nằm trong migrations — loại khỏi lịch sử migration để tránh bị EF "bắt" lại.
        builder.ToTable("outbox_messages", table => table.ExcludeFromMigrations());
        builder.HasKey(message => message.Id);
        builder.Property(message => message.EventType).IsRequired();
        builder.Property(message => message.RoutingKey).IsRequired();
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.CreatedAt).IsRequired();
        builder.Property(message => message.PublishedAt);
        builder.Property(message => message.RetryCount).IsRequired();
        builder.Property(message => message.Error);
        builder.HasIndex(message => message.PublishedAt);
    }
}
