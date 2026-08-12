using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuoteDbContext(DbContextOptions<QuoteDbContext> options)
    : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<Collection> Collections =>
        Set<Collection>();

    public DbSet<User> Users =>
        Set<User>();

    public DbSet<RefreshToken> RefreshTokens =>
        Set<RefreshToken>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Quote>(entity =>
{
    entity.HasKey(q => q.Id);

    entity.Property(q => q.UserId)
        .IsRequired();

    entity.Property(q => q.Author)
        .IsRequired();

    entity.Property(q => q.Text)
        .IsRequired();

    entity.HasOne<User>()
        .WithMany()
        .HasForeignKey(q => q.UserId)
        .OnDelete(DeleteBehavior.Cascade);
});

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Email)
                .IsRequired();

            entity.HasIndex(u => u.Email)
                .IsUnique();

            entity.Property(u => u.PasswordHash)
                .IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Token)
                .IsRequired();

            entity.HasIndex(t => t.Token)
                .IsUnique();

            entity.Property(t => t.ExpiresAt)
                .IsRequired();

            entity.Property(t => t.RevokedAt);

            entity.Property(t => t.ReplacedByToken);

            entity.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

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