using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Users;

namespace Tryum.Infrastructure.Persistence.Configurations;

/// <summary>Cấu hình map <see cref="RefreshToken"/>: token duy nhất, index theo user.</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Token)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(t => t.Token)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token");

        builder.Property(t => t.CreatedByIp)
            .HasMaxLength(64);

        builder.Property(t => t.TenantId)
            .IsRequired();

        builder.HasIndex(t => t.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        builder.Ignore(t => t.IsActive);

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}