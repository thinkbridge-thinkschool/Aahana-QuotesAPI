using QuotesApi.Exceptions;

namespace QuotesApi.Models;

public class Quote
{
    private Quote()
    {
    }

    private Quote(string author, string text)
    {
        Author = author;
        Text = text;
    }

    public int Id { get; private set; }

    public string Author { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public bool IsDeleted { get; private set; }

    public static Quote Create(string author, string text)
    {
        ValidateAuthor(author);
        ValidateText(text);

        return new Quote(
            author.Trim(),
            text.Trim());
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }

    private static void ValidateAuthor(string author)
    {
        if (string.IsNullOrWhiteSpace(author) ||
            author.Trim().Length > 200)
        {
            throw new DomainInvariantException(
                "Author must be between 1 and 200 characters.");
        }
    }

    private static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            text.Trim().Length > 1000)
        {
            throw new DomainInvariantException(
                "Text must be between 1 and 1000 characters.");
        }
    }
}