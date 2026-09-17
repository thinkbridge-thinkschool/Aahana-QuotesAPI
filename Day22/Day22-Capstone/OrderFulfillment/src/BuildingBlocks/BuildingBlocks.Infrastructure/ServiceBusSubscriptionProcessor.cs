using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// One instance per module subscription (Ordering, Inventory, Payments, Shipping, Notifications —
/// see modules/servicebus.bicep's subscriptionEventMap). Registered five times as IHostedService
/// in Program.cs, each with its own subscriptionName — deliberately unkeyed/collection-registered
/// the same way IOutboxStore is (see BuildingBlocks.Infrastructure/IOutboxStore.cs's comment):
/// .NET's hosting infrastructure starts every registered IHostedService, it doesn't pick "the last
/// one", so five registrations really do mean five running processors, not four silently dropped.
///
/// Mirrors InProcessMessageBus's dispatch shape (resolve every IIntegrationEventHandler&lt;TEvent&gt;
/// for the event's runtime type, invoke each in its own DI scope) so a module's handlers behave
/// identically whether the bus underneath is in-process (local dev) or real Service Bus (deployed).
/// </summary>
public class ServiceBusSubscriptionProcessor(
    ServiceBusClient client,
    string topicName,
    string subscriptionName,
    IServiceProvider serviceProvider,
    ILogger<ServiceBusSubscriptionProcessor> logger) : BackgroundService
{
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateProcessor(topicName, subscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        // BackgroundService requires ExecuteAsync to stay running until cancellation; the actual
        // work happens in the event handlers above, driven by the processor's own receive loop.
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var clrTypeName = args.Message.ApplicationProperties.TryGetValue("EventClrType", out var v) ? v as string : null;
        var clrType = clrTypeName is null ? null : Type.GetType(clrTypeName);

        if (clrType is null)
        {
            logger.LogWarning(
                "[{Subscription}] message {MessageId} has no resolvable EventClrType — dead-lettering",
                subscriptionName, args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "UnresolvableEventType");
            return;
        }

        var @event = (IntegrationEvent)JsonSerializer.Deserialize(args.Message.Body, clrType)!;
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(clrType);

        using var scope = serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType).ToList();
        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;

        logger.LogInformation(
            "[{Subscription}] received {EventType} (message {MessageId}) — {HandlerCount} handler(s) registered for {HandlerType}",
            subscriptionName, clrType.Name, args.Message.MessageId, handlers.Count, handlerType.Name);

        foreach (var handler in handlers)
        {
            await (Task)method.Invoke(handler, [@event, args.CancellationToken])!;
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "[{Subscription}] Service Bus processor error ({Source})",
            subscriptionName, args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
