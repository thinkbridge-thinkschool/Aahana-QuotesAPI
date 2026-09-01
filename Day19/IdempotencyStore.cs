using System.Collections.Concurrent;

namespace Day19;

// Tracks which event ids have already been handled, independent of
// Service Bus's own delivery guarantees. At-least-once delivery means
// the same message can arrive more than once (a redelivered
// lock-expired message, a duplicate publish, a consumer crash after
// processing but before completing the message) - this is what makes
// the handler actually idempotent rather than merely "usually fine."
//
// Reserve-then-release, not "mark before work" or "check then mark":
//   - TryReserve is atomic, so two concurrent deliveries of the exact
//     same event id (a genuine duplicate arriving while the original is
//     still mid-flight on another competing-consumer thread) can't both
//     slip past a check before either one records anything - only one
//     wins the reservation.
//   - A handler that fails must call Release, or a message that only
//     ever throws (a poison message) would win the reservation once,
//     fail, and then have every retry wrongly treated as "already
//     handled" and silently completed - it would never fail enough
//     times to actually reach the dead-letter queue.
//
// In-memory only for this demo: it doesn't survive a process restart,
// which is a real gap - see the submission notes for what that means.
public interface IIdempotencyStore
{
    bool TryReserve(string eventId);

    void Release(string eventId);
}

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _reservedEventIds = new();

    public bool TryReserve(string eventId)
    {
        return _reservedEventIds.TryAdd(eventId, 0);
    }

    public void Release(string eventId)
    {
        _reservedEventIds.TryRemove(eventId, out _);
    }
}
