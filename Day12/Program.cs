using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// ============================================================
// DATABASE SETUP
// ============================================================

using (var connection = new SqliteConnection("Data Source=day12.db"))
{
    connection.Open();

    using var command = connection.CreateCommand();

    command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS Quotes
        (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Author TEXT NOT NULL,
            Text TEXT NOT NULL
        );
        """;

    command.ExecuteNonQuery();
}

// ============================================================
// MEDIATR SETUP
// ============================================================

var services = new ServiceCollection();

services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<
        CreateQuoteCommandHandler>();
});

var provider = services.BuildServiceProvider();

var mediator = provider.GetRequiredService<IMediator>();

// ============================================================
// COMMAND — WRITE MODEL
// ============================================================

var commandResult = await mediator.Send(
    new CreateQuoteCommand(
        "Albert Einstein",
        "Life is like riding a bicycle."));

Console.WriteLine("===== COMMAND / WRITE MODEL =====");
Console.WriteLine($"Created quote ID: {commandResult.Id}");
Console.WriteLine($"Author: {commandResult.Author}");
Console.WriteLine($"Text: {commandResult.Text}");

// ============================================================
// QUERY — READ MODEL
// ============================================================

var readModel = await mediator.Send(
    new GetQuoteReadModelQuery(commandResult.Id));

Console.WriteLine();
Console.WriteLine("===== QUERY / READ MODEL =====");
Console.WriteLine($"ID: {readModel.Id}");
Console.WriteLine($"Display: {readModel.DisplayText}");

// ============================================================
// COMMAND
// ============================================================

public sealed record CreateQuoteCommand(
    string Author,
    string Text) : IRequest<QuoteWriteResult>;

// ============================================================
// WRITE MODEL RESULT
// ============================================================

public sealed record QuoteWriteResult(
    int Id,
    string Author,
    string Text);

// ============================================================
// COMMAND HANDLER
// ============================================================

public sealed class CreateQuoteCommandHandler
    : IRequestHandler<CreateQuoteCommand, QuoteWriteResult>
{
    public async Task<QuoteWriteResult> Handle(
        CreateQuoteCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Author))
        {
            throw new ArgumentException(
                "Author is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException(
                "Quote text is required.");
        }

        var author = request.Author.Trim();
        var text = request.Text.Trim();

        using var connection =
            new SqliteConnection(
                "Data Source=day12.db");

        await connection.OpenAsync(
            cancellationToken);

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO Quotes (Author, Text)
            VALUES ($author, $text);

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue(
            "$author",
            author);

        command.Parameters.AddWithValue(
            "$text",
            text);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        var id = Convert.ToInt32(result);

        return new QuoteWriteResult(
            id,
            author,
            text);
    }
}

// ============================================================
// QUERY
// ============================================================

public sealed record GetQuoteReadModelQuery(
    int Id) : IRequest<QuoteReadModel>;

// ============================================================
// READ MODEL
// ============================================================

public sealed record QuoteReadModel(
    int Id,
    string DisplayText);

// ============================================================
// QUERY HANDLER
// ============================================================

public sealed class GetQuoteReadModelQueryHandler
    : IRequestHandler<GetQuoteReadModelQuery, QuoteReadModel>
{
    public async Task<QuoteReadModel> Handle(
        GetQuoteReadModelQuery request,
        CancellationToken cancellationToken)
    {
        using var connection =
            new SqliteConnection(
                "Data Source=day12.db");

        await connection.OpenAsync(
            cancellationToken);

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT Id, Author, Text
            FROM Quotes
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            request.Id);

        using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Quote not found.");
        }

        var id = reader.GetInt32(0);
        var author = reader.GetString(1);
        var text = reader.GetString(2);

        var displayText =
            $"\"{text}\" — {author}";

        return new QuoteReadModel(
            id,
            displayText);
    }
}