using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using QuotesApi.Abstractions;
using QuotesApi.Dtos;
using QuotesApi.Exceptions;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

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
                page,
                size,
                cancellationToken);

            return Results.Ok(quotes);
        });

        // Day 5: Intentionally slow endpoint for observability exercise
        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            await Task.Delay(1500, cancellationToken);

            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        // Day 22: fact enrichment via an outbound dependency wrapped in a
        // Polly resilience pipeline (bulkhead, timeout, retry, circuit
        // breaker - see HttpResilienceExtensions.AddFactApiResilience).
        // Enrichment is best-effort: a failing/overloaded/broken-circuit
        // fact API degrades to a 503 for this endpoint, it never breaks
        // the quote read itself.
        group.MapGet("/{id:int}/fact", async (
            int id,
            IQuoteRepository repository,
            IFunFactProvider factProvider,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            if (quote is null)
            {
                return Results.NotFound();
            }

            try
            {
                var fact = await factProvider.GetFactAsync(
                    cancellationToken);

                return Results.Ok(new { quote, fact });
            }
            catch (Exception ex) when (
                ex is BrokenCircuitException
                    or TimeoutRejectedException
                    or RateLimiterRejectedException
                    or HttpRequestException)
            {
                var logger = loggerFactory.CreateLogger(
                    "QuotesApi.QuoteEndpoints");

                logger.LogWarning(
                    ex,
                    "Fact API unavailable for quote {QuoteId}",
                    id);

                return Results.Problem(
                    title: "Fact service unavailable",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        group.MapPost("/", async (
            CreateQuoteRequest request,
            ClaimsPrincipal user,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var userIdClaim = user.FindFirst(
                JwtRegisteredClaimNames.Sub)?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var validationContext =
                new ValidationContext(request);

            var validationResults =
                new List<ValidationResult>();

            if (!Validator.TryValidateObject(
                    request,
                    validationContext,
                    validationResults,
                    true))
            {
                var errors = validationResults
                    .GroupBy(
                        x => x.MemberNames.FirstOrDefault()
                             ?? "request")
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(
                            x => x.ErrorMessage
                                ?? "Invalid value").ToArray());

                return Results.ValidationProblem(errors);
            }

            Quote quote;

            try
            {
                quote = Quote.Create(
                    userId,
                    request.Author,
                    request.Text);
            }
            catch (DomainInvariantException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["quote"] = [ex.Message]
                    });
            }

            var created = await repository.AddAsync(
                quote,
                cancellationToken);

            var logger = loggerFactory.CreateLogger(
                "QuotesApi.QuoteEndpoints");

            logger.LogInformation(
                "Created quote {QuoteId} by {Author} for user {UserId}",
                created.Id,
                created.Author,
                userId);

            return Results.Created(
                $"/api/quotes/{created.Id}",
                created);
        })
        .RequireAuthorization("can-edit-quotes");

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
                id,
                cancellationToken);

            if (!deleted)
            {
                return Results.NotFound();
            }

            var logger = loggerFactory.CreateLogger(
                "QuotesApi.QuoteEndpoints");

            logger.LogInformation(
                "Deleted quote {QuoteId}",
                id);

            return Results.NoContent();
        })
        .RequireAuthorization();

        // Collection endpoints

        var collections = app.MapGroup("/api/collections");

        collections.MapPost("/", async (
            CreateCollectionRequest request,
            ICollectionRepository repository,
            CancellationToken cancellationToken) =>
        {
            var validationContext =
                new ValidationContext(request);

            var validationResults =
                new List<ValidationResult>();

            if (!Validator.TryValidateObject(
                    request,
                    validationContext,
                    validationResults,
                    true))
            {
                var errors = validationResults
                    .GroupBy(
                        x => x.MemberNames.FirstOrDefault()
                             ?? "request")
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(
                            x => x.ErrorMessage
                                ?? "Invalid value").ToArray());

                return Results.ValidationProblem(errors);
            }

            var collection = new Collection(
                request.Name,
                request.OwnerId);

            var created = await repository.Add(
                collection,
                cancellationToken);

            return Results.Created(
                $"/api/collections/{created.Id}",
                created);
        });

        collections.MapPost(
            "/{id:int}/items",
            async (
                int id,
                int quoteId,
                ICollectionRepository collectionRepository,
                IQuoteRepository quoteRepository,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                var collection =
                    await collectionRepository.GetById(
                        id,
                        cancellationToken);

                if (collection is null)
                {
                    return Results.NotFound();
                }

                var quote =
                    await quoteRepository.GetByIdAsync(
                        quoteId,
                        cancellationToken);

                if (quote is null)
                {
                    return Results.NotFound();
                }

                collection.AddItem(
                    quoteId,
                    clock.UtcNow.UtcDateTime);

                await collectionRepository.Update(
                    collection,
                    cancellationToken);

                return Results.NoContent();
            });

        collections.MapDelete(
            "/{id:int}/items/{quoteId:int}",
            async (
                int id,
                int quoteId,
                ICollectionRepository repository,
                CancellationToken cancellationToken) =>
            {
                var collection =
                    await repository.GetById(
                        id,
                        cancellationToken);

                if (collection is null)
                {
                    return Results.NotFound();
                }

                collection.RemoveItem(quoteId);

                await repository.Update(
                    collection,
                    cancellationToken);

                return Results.NoContent();
            });

        return app;
    }
}