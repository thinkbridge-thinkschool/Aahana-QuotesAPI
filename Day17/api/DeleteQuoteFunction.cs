using System.Net;

using Azure.Core;
using Azure.Identity;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Day17Api;

// SWA-linked backend for the one write path that runs as a service call
// rather than an interactive user action: moderation delete. The Angular
// app still authenticates end users with their own JWT for reads/writes;
// this function authenticates itself to the Week-1 QuotesApi with its
// Managed Identity, so a moderation delete triggered through the SWA
// "/api/*" route never needs a stored client secret.
public class DeleteQuoteFunction
{
    private readonly DefaultAzureCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DeleteQuoteFunction> _logger;

    public DeleteQuoteFunction(
        DefaultAzureCredential credential,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DeleteQuoteFunction> logger)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [Function("DeleteQuote")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "quotes/{id:int}")]
            HttpRequestData request,
        int id,
        CancellationToken cancellationToken)
    {
        var apiBaseUrl = _configuration["QuotesApi:BaseUrl"]
            ?? throw new InvalidOperationException(
                "QuotesApi:BaseUrl is not configured.");

        var entraAudience = _configuration["QuotesApi:EntraAudience"]
            ?? throw new InvalidOperationException(
                "QuotesApi:EntraAudience is not configured.");

        var tokenRequestContext = new TokenRequestContext(
            new[] { $"{entraAudience}/.default" });

        AccessToken accessToken;

        try
        {
            accessToken = await _credential.GetTokenAsync(
                tokenRequestContext,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to acquire a Managed Identity token for {Audience}",
                entraAudience);

            var failed = request.CreateResponse(HttpStatusCode.BadGateway);
            await failed.WriteStringAsync("Unable to authenticate to QuotesApi.");
            return failed;
        }

        using var httpClient = _httpClientFactory.CreateClient("QuotesApi");

        using var upstreamRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{apiBaseUrl}/api/quotes/{id}");

        upstreamRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                accessToken.Token);

        var upstreamResponse = await httpClient.SendAsync(
            upstreamRequest,
            cancellationToken);

        var response = request.CreateResponse(upstreamResponse.StatusCode);

        _logger.LogInformation(
            "Deleted quote {QuoteId} via Managed Identity call, upstream status {Status}",
            id,
            (int)upstreamResponse.StatusCode);

        return response;
    }
}
