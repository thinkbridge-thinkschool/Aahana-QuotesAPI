using Microsoft.EntityFrameworkCore;

namespace Day20;

public class OutboxDbContext(DbContextOptions<OutboxDbContext> options)
    : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(q => q.Id);

            entity.Property(q => q.Author).IsRequired();
            entity.Property(q => q.Text).IsRequired();
            entity.Property(q => q.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            // SequenceNumber, not Id, is the EF/SQLite primary key -
            // that's what makes it a real database-assigned
            // auto-increment (SQLite only auto-increments a column
            // that IS the table's rowid, which requires it to be the
            // single-column INTEGER PRIMARY KEY). Id stays as the
            // business/idempotency key, separately unique-indexed.
            entity.HasKey(m => m.SequenceNumber);

            entity.HasIndex(m => m.Id).IsUnique();

            entity.Property(m => m.Type).IsRequired();
            entity.Property(m => m.Payload).IsRequired();
            entity.Property(m => m.CreatedAt).IsRequired();
            entity.Property(m => m.Sent).IsRequired();

            entity.HasOne(m => m.Quote)
                .WithMany(q => q.OutboxMessages)
                .HasForeignKey(m => m.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);

            // The relay's whole job is "find unsent rows" - this index
            // is what makes that a cheap query instead of a table scan
            // once the outbox has any real history in it.
            entity.HasIndex(m => m.Sent);
        });
    }
}
