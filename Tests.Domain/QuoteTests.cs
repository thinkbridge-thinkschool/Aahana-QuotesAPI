using FluentAssertions;
using QuotesApi.Exceptions;
using QuotesApi.Models;

namespace Tests.Domain;

public class QuoteTests
{
    [Fact]
    public void Create_with_empty_author_throws()
    {
        // Arrange
        var userId = 1;
        var author = "";
        var text = "Valid quote";

        // Act
        var act = () => Quote.Create(userId, author, text);

        // Assert
        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_with_whitespace_author_throws()
    {
        // Arrange
        var userId = 1;
        var author = "   ";
        var text = "Valid quote";

        // Act
        var act = () => Quote.Create(userId, author, text);

        // Assert
        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_with_author_longer_than_200_characters_throws()
    {
        // Arrange
        var userId = 1;
        var author = new string('A', 201);
        var text = "Valid quote";

        // Act
        var act = () => Quote.Create(userId, author, text);

        // Assert
        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_with_empty_text_throws()
    {
        // Arrange
        var userId = 1;
        var author = "Author";
        var text = "";

        // Act
        var act = () => Quote.Create(userId, author, text);

        // Assert
        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_with_whitespace_text_throws()
    {
        // Arrange
        var userId = 1;
        var author = "Author";
        var text = "   ";

        // Act
        var act = () => Quote.Create(userId, author, text);

        // Assert
        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_with_text_longer_than_1000_characters_throws()
    {
        // Arrange
        var userId = 1;
        var author = "Author";
        var text = new string('A', 1001);

        // Act
        var act = () => Quote.Create(userId, author, text);

        // Assert
        act.Should()
            .Throw<DomainInvariantException>();
    }

    [Fact]
    public void Create_returns_valid_quote()
    {
        // Arrange
        var userId = 1;
        var author = "Author";
        var text = "A valid quote";

        // Act
        var quote = Quote.Create(userId, author, text);

        // Assert
        quote.UserId.Should().Be(1);
        quote.Author.Should().Be("Author");
        quote.Text.Should().Be("A valid quote");
        quote.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_trims_author()
    {
        // Arrange
        var userId = 1;
        var author = "  Author  ";
        var text = "Valid quote";

        // Act
        var quote = Quote.Create(userId, author, text);

        // Assert
        quote.Author.Should().Be("Author");
    }

    [Fact]
    public void Create_trims_text()
    {
        // Arrange
        var userId = 1;
        var author = "Author";
        var text = "  Valid quote  ";

        // Act
        var quote = Quote.Create(userId, author, text);

        // Assert
        quote.Text.Should().Be("Valid quote");
    }

    [Fact]
    public void Create_preserves_user_id()
    {
        // Arrange
        var userId = 42;
        var author = "Author";
        var text = "Valid quote";

        // Act
        var quote = Quote.Create(userId, author, text);

        // Assert
        quote.UserId.Should().Be(42);
    }

    [Fact]
    public void SoftDelete_marks_quote_as_deleted()
    {
        // Arrange
        var quote = Quote.Create(
            1,
            "Author",
            "A valid quote");

        // Act
        quote.SoftDelete();

        // Assert
        quote.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void SoftDelete_can_be_called_without_changing_content()
    {
        // Arrange
        var quote = Quote.Create(
            1,
            "Author",
            "A valid quote");

        // Act
        quote.SoftDelete();

        // Assert
        quote.Author.Should().Be("Author");
        quote.Text.Should().Be("A valid quote");
        quote.UserId.Should().Be(1);
    }
}