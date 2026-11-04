using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Customers;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Code).HasMaxLength(40).IsRequired();
        builder.Property(customer => customer.Name).HasMaxLength(200).IsRequired();
        builder.Property(customer => customer.Phone).HasMaxLength(40);
        builder.Property(customer => customer.Email).HasMaxLength(254);
        builder.Property(customer => customer.Address).HasMaxLength(500);
        builder.Property(customer => customer.Region).HasMaxLength(160);
        builder.Property(customer => customer.Ward).HasMaxLength(160);
        builder.Property(customer => customer.GroupName).HasMaxLength(120);
        builder.Property(customer => customer.Gender).HasMaxLength(20);
        builder.Property(customer => customer.Facebook).HasMaxLength(255);
        builder.Property(customer => customer.AssignedTo).HasMaxLength(200);
        builder.Property(customer => customer.Note).HasMaxLength(2000);
        builder.Property(customer => customer.CompanyName).HasMaxLength(250);
        builder.Property(customer => customer.TaxCode).HasMaxLength(80);
        builder.Property(customer => customer.InvoiceAddress).HasMaxLength(500);
        builder.Property(customer => customer.Points).HasDefaultValue(0);
        builder.Property(customer => customer.DebtAmount).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.HasIndex(customer => new { customer.TenantId, customer.Code }).IsUnique();
        builder.HasIndex(customer => new { customer.TenantId, customer.Phone });
        builder.HasIndex(customer => new { customer.TenantId, customer.Name });
    }
}
