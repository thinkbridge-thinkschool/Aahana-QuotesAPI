using QuotesApi.Exceptions;

namespace QuotesApi.Models;

public class Collection
{
    private readonly List<CollectionItem> _items = [];

    private Collection()
    {
    }

    public Collection(
        string name,
        int ownerId)
    {
        ValidateName(name);

        Name = name;
        OwnerId = ownerId;
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int OwnerId { get; private set; }

    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    public void AddItem(int quoteId)
    {
        if (_items.Count >= 50)
        {
            throw new DomainInvariantException(
                "A collection cannot contain more than 50 items.");
        }

        if (_items.Any(x => x.QuoteId == quoteId))
        {
            throw new DomainInvariantException(
                $"Quote {quoteId} is already in this collection.");
        }

        _items.Add(
            new CollectionItem(
                quoteId,
                DateTime.UtcNow));
    }

    public void RemoveItem(int quoteId)
    {
        var item = _items.FirstOrDefault(
            x => x.QuoteId == quoteId);

        if (item is not null)
        {
            _items.Remove(item);
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Trim().Length < 3 ||
            name.Trim().Length > 80)
        {
            throw new DomainInvariantException(
                "Collection name must be between 3 and 80 characters.");
        }
    }
}