using System.ComponentModel.DataAnnotations;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (
            int page,
            int size,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            page = page < 1 ? 1 : page;
            size = size is < 1 or > 100 ? 10 : size;

            var quotes = await repository.GetPagedAsync(
                page, size, cancellationToken);

            return Results.Ok(quotes);
        });

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id, cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        group.MapPost("/", async (
            CreateQuoteRequest request,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var validationContext = new ValidationContext(request);
            var validationResults = new List<ValidationResult>();

            if (!Validator.TryValidateObject(
                    request,
                    validationContext,
                    validationResults,
                    true))
            {
                var errors = validationResults
                    .GroupBy(x => x.MemberNames.FirstOrDefault() ?? "request")
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(
                            x => x.ErrorMessage ?? "Invalid value").ToArray());

                return Results.ValidationProblem(errors);
            }

            var quote = new Quote
            {
                Author = request.Author,
                Text = request.Text
            };

            var created = await repository.AddAsync(
                quote, cancellationToken);

            var logger = loggerFactory.CreateLogger(
                "QuotesApi.QuoteEndpoints");

            logger.LogInformation(
                "Created quote {QuoteId} by {Author}",
                created.Id,
                created.Author);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
                id, cancellationToken);

            if (!deleted)
                return Results.NotFound();

            var logger = loggerFactory.CreateLogger(
                "QuotesApi.QuoteEndpoints");

            logger.LogInformation(
                "Deleted quote {QuoteId}",
                id);

            return Results.NoContent();
        });

        return app;
    }
}