using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Domain;

namespace Ordering.Infrastructure;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);

        builder.OwnsMany(o => o.Lines, lines =>
        {
            lines.ToTable("OrderLines");
            lines.WithOwner().HasForeignKey("OrderId");
            lines.HasKey(l => l.Id);
            lines.Property(l => l.Sku).HasMaxLength(64).IsRequired();

            lines.OwnsOne(l => l.UnitPrice, price =>
            {
                price.Property(p => p.Amount).HasColumnName("UnitPriceAmount").HasPrecision(18, 2);
                price.Property(p => p.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3);
            });
        });

        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.DomainEvents);
    }
}
