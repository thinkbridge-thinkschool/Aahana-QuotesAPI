using FluentAssertions;
using QuotesApi.Exceptions;
using QuotesApi.Models;

namespace Tests.Domain;

public class QuoteTests
{
    [Fact]
    public void Create_with_empty_author_throws()
    {
        var act = () => Quote.Create("", "Valid quote");

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_with_author_longer_than_200_characters_throws()
    {
        var author = new string('A', 201);

        var act = () => Quote.Create(author, "Valid quote");

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_with_empty_text_throws()
    {
        var act = () => Quote.Create("Author", "");

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_with_text_longer_than_1000_characters_throws()
    {
        var text = new string('A', 1001);

        var act = () => Quote.Create("Author", text);

        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_returns_valid_quote()
    {
        var quote = Quote.Create("Author", "A valid quote");

        quote.Author.Should().Be("Author");
        quote.Text.Should().Be("A valid quote");
        quote.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void SoftDelete_marks_quote_as_deleted()
    {
        var quote = Quote.Create("Author", "A valid quote");

        quote.SoftDelete();

        quote.IsDeleted.Should().BeTrue();
    }
}