using QuotesApi.Abstractions;

namespace QuotesApi.Infrastructure;

public class QuoteFormatter : IQuoteFormatter
{
    public string Format(string author, string text)
    {
        return $"\"{text}\" — {author}";
    }
}