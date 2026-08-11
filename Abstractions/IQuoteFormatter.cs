namespace QuotesApi.Abstractions;

public interface IQuoteFormatter
{
    string Format(string author, string text);
}