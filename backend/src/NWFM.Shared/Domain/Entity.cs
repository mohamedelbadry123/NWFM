namespace NWFM.Shared.Domain;

/// <summary>
/// Base class for all domain entities. Provides identity and timestamp tracking.
/// Concrete entities expose a static factory method and keep setters private to enforce
/// invariants through the aggregate root — never through public property assignment.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }
    public DateTime CreatedAt { get; protected set; }
    public DateTime UpdatedAt { get; protected set; }

    /// <summary>
    /// Required by EF Core to materialise entities from the database.
    /// Must not be used in application code.
    /// </summary>
    protected Entity()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    protected void SetUpdated(DateTime utcNow) => UpdatedAt = utcNow;
}
