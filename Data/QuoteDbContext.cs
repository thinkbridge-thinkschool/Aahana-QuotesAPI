using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuoteDbContext(DbContextOptions<QuoteDbContext> options)
    : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<Collection> Collections =>
        Set<Collection>();
    
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(c => c.OwnerId)
                .IsRequired();

            entity.OwnsMany(
                c => c.Items,
                item =>
                {
                    item.ToTable("CollectionItems");

                    item.WithOwner()
                        .HasForeignKey("CollectionId");

                    item.Property(i => i.QuoteId)
                        .IsRequired();

                    item.Property(i => i.AddedAt)
                        .IsRequired();

                    item.HasKey(
                        "CollectionId",
                        nameof(CollectionItem.QuoteId));
                });
        });
    }
}