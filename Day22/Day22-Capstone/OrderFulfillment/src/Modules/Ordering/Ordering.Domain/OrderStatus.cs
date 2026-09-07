namespace Ordering.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
    PaymentReceived,
    Shipped,
    Cancelled,
}
