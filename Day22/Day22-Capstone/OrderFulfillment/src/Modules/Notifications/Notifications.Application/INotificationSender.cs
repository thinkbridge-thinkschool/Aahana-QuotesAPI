namespace Notifications.Application;

/// <summary>
/// Addressed by orderId, not customerId: resolving an order to a contact address is a read-model
/// concern this scaffold doesn't build out. A real implementation would look the customer up
/// through its own denormalized projection, never by reaching into Ordering's database.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(Guid orderId, string message, CancellationToken ct = default);
}
