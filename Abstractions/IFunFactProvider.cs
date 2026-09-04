namespace QuotesApi.Abstractions;

public interface IFunFactProvider
{
    Task<string> GetFactAsync(CancellationToken cancellationToken);
}
