using Microsoft.AspNetCore.Mvc;
using QuotesApi.Exceptions;

namespace QuotesApi.Middleware;

public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainInvariantException ex)
        {
            logger.LogWarning(
                "Domain invariant violated: {Message}",
                ex.Message);

            context.Response.StatusCode = 400;
            context.Response.ContentType =
                "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = 400,
                Title = "Domain invariant violated.",
                Detail = ex.Message
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unhandled exception.");

            context.Response.StatusCode = 500;
            context.Response.ContentType =
                "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = 500,
                Title = "An unexpected error occurred.",
                Detail =
                    "The server encountered an unexpected error."
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}