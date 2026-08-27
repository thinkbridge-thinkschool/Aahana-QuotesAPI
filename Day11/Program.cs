using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

const string connectionString = "Data Source=day11.db";

app.MapGet("/slow", () =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    // FIX 1: Add an index for Author lookups.
    using (var indexCommand = connection.CreateCommand())
    {
        indexCommand.CommandText =
            "CREATE INDEX IF NOT EXISTS IX_Quotes_Author ON Quotes(Author);";

        indexCommand.ExecuteNonQuery();
    }

    // FIX 2: Eliminate the N+1 pattern.
    // One query fetches all required rows.
    var quotes = new List<object>();

    using (var command = connection.CreateCommand())
    {
        command.CommandText =
            """
            SELECT Id, Author, Text
            FROM Quotes
            WHERE IsDeleted = 0
            ORDER BY Author, Id;
            """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            quotes.Add(new
            {
                Id = reader.GetInt32(0),
                Author = reader.GetString(1),
                Text = reader.GetString(2)
            });
        }
    }

    var authors = quotes
        .Select(q => q.GetType().GetProperty("Author")!.GetValue(q)!.ToString())
        .Distinct()
        .Count();

    return Results.Ok(new
    {
        Authors = authors,
        Quotes = quotes.Count,
        Queries = 2
    });
});

app.MapGet("/plan", () =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    using var command = connection.CreateCommand();

    command.CommandText =
        """
        EXPLAIN QUERY PLAN
        SELECT Id, Author, Text
        FROM Quotes
        WHERE IsDeleted = 0
        ORDER BY Author, Id;
        """;

    using var reader = command.ExecuteReader();

    var plan = new List<string>();

    while (reader.Read())
    {
        plan.Add(reader.GetString(3));
    }

    return Results.Ok(plan);
});
app.MapGet("/", () => "Day 11 performance API is running.");


app.Run();