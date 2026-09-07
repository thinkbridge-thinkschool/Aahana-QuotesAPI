namespace BuildingBlocks.Domain;

public abstract class Entity<TId>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && other.GetType() == GetType() && Id.Equals(other.Id);

    public override int GetHashCode() => Id.GetHashCode();
}
