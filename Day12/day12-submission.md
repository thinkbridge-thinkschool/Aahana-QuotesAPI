\# Day 12 — Read Models + CQRS-lite



\## Command Handler



The command represents the write path. It validates the author and quote text, normalizes the values with `Trim()`, and persists the quote.



```csharp

public sealed record CreateQuoteCommand(

&#x20;   string Author,

&#x20;   string Text) : IRequest<QuoteWriteResult>;



public sealed class CreateQuoteCommandHandler

&#x20;   : IRequestHandler<CreateQuoteCommand, QuoteWriteResult>

{

&#x20;   public async Task<QuoteWriteResult> Handle(

&#x20;       CreateQuoteCommand request,

&#x20;       CancellationToken cancellationToken)

&#x20;   {

&#x20;       if (string.IsNullOrWhiteSpace(request.Author))

&#x20;       {

&#x20;           throw new ArgumentException("Author is required.");

&#x20;       }



&#x20;       if (string.IsNullOrWhiteSpace(request.Text))

&#x20;       {

&#x20;           throw new ArgumentException("Quote text is required.");

&#x20;       }



&#x20;       var author = request.Author.Trim();

&#x20;       var text = request.Text.Trim();



&#x20;       using var connection =

&#x20;           new SqliteConnection("Data Source=day12.db");



&#x20;       await connection.OpenAsync(cancellationToken);



&#x20;       using var command = connection.CreateCommand();



&#x20;       command.CommandText =

&#x20;           """

&#x20;           INSERT INTO Quotes (Author, Text)

&#x20;           VALUES ($author, $text);



&#x20;           SELECT last\_insert\_rowid();

&#x20;           """;



&#x20;       command.Parameters.AddWithValue("$author", author);

&#x20;       command.Parameters.AddWithValue("$text", text);



&#x20;       var result =

&#x20;           await command.ExecuteScalarAsync(cancellationToken);



&#x20;       var id = Convert.ToInt32(result);



&#x20;       return new QuoteWriteResult(id, author, text);

&#x20;   }

}

