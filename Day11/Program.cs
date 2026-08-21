using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

const string connectionString = "Data Source=day11.db";

app.MapGet("/slow", () =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    var authors = new List<string>();

    using (var command = connection.CreateCommand())
    {
        command.CommandText =
            "SELECT DISTINCT Author FROM Quotes ORDER BY Author;";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            authors.Add(reader.GetString(0));
        }
    }

    var quotes = new List<object>();

    foreach (var author in authors)
    {
        using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT Id, Author, Text FROM Quotes WHERE Author = $author;";

        command.Parameters.AddWithValue("$author", author);

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

    return Results.Ok(new
    {
        Authors = authors.Count,
        Quotes = quotes.Count,
        Queries = authors.Count + 1
    });
});

app.MapGet("/", () => "Day 11 performance API is running.");

app.Run();