using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// Resolves every registered IIntegrationEventHandler&lt;TEvent&gt; for the event's runtime type
/// and invokes them in a fresh DI scope per handler, so one module's failing handler can't share
/// (or poison) another module's DbContext/unit of work for the same event delivery.
/// </summary>
public class InProcessMessageBus(IServiceProvider serviceProvider) : IMessageBus
{
    public async Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(@event.GetType());

        using var scope = serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;
            await (Task)method.Invoke(handler, [@event, ct])!;
        }
    }
}
