# Database

## Technology

**SQLite** (`Microsoft.EntityFrameworkCore.Sqlite`) for local development and the main application
(`quotes.db`, gitignored — never committed). The integration test suite (`Quotes.Tests.Integration`)
deliberately runs against a **real SQL Server container** via Testcontainers instead, to catch
provider-specific behavior differences before they'd surface in a real deployment. Both are real,
current parts of this project — SQLite is not a "toy" stand-in for a "real" database elsewhere; it's
the intentional local/dev choice, with SQL Server used specifically where integration-test fidelity
matters more than setup cost.

## DbContext

`Data/QuoteDbContext.cs` exposes four `DbSet`s: `Quotes`, `Collections`, `Users`, `RefreshTokens`.
`CollectionItem` has no `DbSet` of its own — it's configured as an EF Core **owned entity** of
`Collection` (see below), not an independent aggregate.

Connection string resolution (`Extensions/InfrastructureExtensions.cs`):
`configuration.GetConnectionString("QuotesDb") ?? "Data Source=quotes.db"` — falls back to a local
file if nothing is configured, which is why the app runs with zero database setup out of the box.

## Schema (current, from `Migrations/QuoteDbContextModelSnapshot.cs`)

| Table | Key | Notable columns | Relationships |
|---|---|---|---|
| `Quotes` | `Id` (PK, autoincrement) | `Author`, `Text`, `IsDeleted` (soft-delete flag), `UserId` | FK `UserId → Users.Id`, cascade delete |
| `Users` | `Id` (PK, autoincrement) | `Email` (unique index), `PasswordHash` | — |
| `RefreshTokens` | `Id` (PK, autoincrement) | `Token` (unique index, SHA-256 hash — never the raw token), `ExpiresAt`, `RevokedAt?`, `ReplacedByToken?` | FK `UserId → Users.Id`, cascade delete |
| `Collections` | `Id` (PK, autoincrement) | `Name` (max 80 chars), `OwnerId` | — |
| `CollectionItems` | **composite PK** `(CollectionId, QuoteId)` | `AddedAt` | FK `CollectionId → Collections.Id`, cascade delete. Owned by `Collection`, not independently queryable. |

## Schema evolution (migrations, in order)

1. **`InitialCreate`** — `Quotes(Id, Author, Text)`.
2. **`AddCollections`** — adds `Collections` and `CollectionItems` (composite key from day one).
3. **`AddUsers`** — adds `Quotes.IsDeleted` (default `false`) and the `Users` table.
4. **`AddRefreshTokens`** — adds `RefreshTokens`, a unique index on `Users.Email`, a unique index on
   `RefreshTokens.Token`, and an index on `RefreshTokens.UserId`.
5. **`AddQuoteUserId`** — adds `Quotes.UserId` (defaulted, then backfilled to `1` for pre-existing rows
   via raw SQL in the migration), an index, and the FK to `Users`.

Migrations are applied automatically on startup — `Program.cs` calls `db.Database.MigrateAsync()`
inside a scoped service provider before the app starts handling requests. There is no manual
`dotnet ef database update` step required to run the app locally.

## Known technical consideration: the composite-key warning

On every startup you'll see:

```
warn: The entity type 'CollectionItem' has composite key '{'CollectionId', 'QuoteId'}' which is
      configured to use generated values. SQLite does not support generated values on composite keys.
```

**Root cause:** EF Core's default convention marks `int` key properties as store-generated
(`ValueGeneratedOnAdd()`) unless told otherwise. `CollectionItem.QuoteId` is an `int` and is half of
the `(CollectionId, QuoteId)` composite key, so the convention applies to it too — even though
`QuoteId` is always supplied explicitly by application code (`Collection.AddItem(quoteId, addedAt)`),
never left for the database to generate. SQLite additionally has no mechanism to autoincrement one
column of a multi-column key, which is what triggers the specific warning text.

**Practical impact:** none observed — `CollectionItem` rows are never inserted without an explicit
`QuoteId`, so there's no actual risk of silent data loss or duplicate-key collisions from this warning.
It's a real, known consideration worth being able to explain in an interview (it demonstrates
understanding of EF Core's key-generation conventions and SQLite's constraints), not a bug that was
silently patched over. Fixing it cleanly would mean calling `.ValueGeneratedNever()` on `QuoteId` in
`OnModelCreating` — left undone here deliberately, since it would change the schema/model configuration
without a functional bug to justify it, per this project's own "don't change the database schema
without reason" rule.

## How a request reaches the database

`Endpoint → Repository interface → Repository implementation → DbContext → EF Core → SQLite`

Example: `GET /api/quotes` → `QuoteEndpointExtensions.MapQuoteEndpoints` → `IQuoteRepository.GetPagedAsync`
→ `QuoteRepository` (`AsNoTracking()`, filters `!IsDeleted`, orders by `Id`, applies `Skip`/`Take`) →
`QuoteDbContext.Quotes` → generated SQL against `quotes.db`.

Both `QuoteRepository` and `CollectionRepository` use `AsNoTracking()` for reads (better performance,
no accidental entity mutation) and rely on EF Core's change tracker only for writes.

**Soft delete vs hard delete — this differs by entity, on purpose:**
- `Quote.SoftDelete()` — sets `IsDeleted = true`. `DELETE /api/quotes/{id}` never physically removes a
  row from `Quotes`. `GetPagedAsync`/`GetByIdAsync` both filter `!IsDeleted`, so deleted quotes simply
  stop appearing.
- `CollectionRepository.Delete` — a genuine hard delete (`Remove` + `SaveChanges`). Collections do not
  have a soft-delete flag in the schema.
