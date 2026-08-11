using FluentAssertions;
using QuotesApi.Exceptions;
using QuotesApi.Models;

namespace Tests.Domain;

public class CollectionTests
{
    private static readonly DateTime AddedAt =
        new(2026, 1, 1);

    [Fact]
    public void Empty_name_throws()
    {
        var act = () => new Collection("", 1);

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Name_longer_than_80_characters_throws()
    {
        var name = new string('A', 81);

        var act = () => new Collection(name, 1);

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Adding_51st_item_throws()
    {
        var collection = new Collection("Test", 1);

        for (var quoteId = 1; quoteId <= 50; quoteId++)
        {
            collection.AddItem(quoteId, AddedAt);
        }

        var act = () => collection.AddItem(51, AddedAt);

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Adding_duplicate_quote_id_throws()
    {
        var collection = new Collection("Test", 1);

        collection.AddItem(1, AddedAt);

        var act = () => collection.AddItem(1, AddedAt);

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Removing_nonexistent_item_throws()
    {
        var collection = new Collection("Test", 1);

        var act = () => collection.RemoveItem(999);

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Adding_then_removing_item_leaves_zero_items()
    {
        var collection = new Collection("Test", 1);

        collection.AddItem(1, AddedAt);
        collection.RemoveItem(1);

        collection.Items.Should().BeEmpty();
    }
}