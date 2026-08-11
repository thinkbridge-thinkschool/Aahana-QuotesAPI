using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

public class CancellationTests
{
    [Fact]
    public async Task CollectionEndpoint_Honors_Request_Cancellation()
    {
        var repository = new BlockingCollectionRepository();

        await using var factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<ICollectionRepository>();

                        services.AddSingleton<ICollectionRepository>(
                            repository);
                    });
                });

        using var client = factory.CreateClient();

        using var cts = new CancellationTokenSource();

        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/collections/1/items/1");

        var requestTask = client.SendAsync(
            request,
            cts.Token);

        await repository.RequestStarted;

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await requestTask);
    }

    private sealed class BlockingCollectionRepository
        : ICollectionRepository
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestStarted => _started.Task;

        public async Task<Collection?> GetById(
            int id,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            return null;
        }

        public Task<Collection> Add(
            Collection collection,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task Update(
            Collection collection,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task Delete(
            Collection collection,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}