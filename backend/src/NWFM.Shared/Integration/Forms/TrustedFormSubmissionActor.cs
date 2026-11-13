namespace NWFM.Shared.Integration.Forms;

/// <summary>Request-scoped, server-only attribution for an already-authorized workspace demo action.
/// This is never populated from a form submission DTO or a client header.</summary>
public sealed class TrustedFormSubmissionActor
{
    private readonly AsyncLocal<Attribution?> _current = new();
    public sealed record Attribution(Guid WorkItemId, Guid AuditActorId, Guid EffectiveActorId, string DisplayName);

    public IDisposable BeginAuthorizedDemo(Guid workItemId, Guid auditActorId, Guid effectiveActorId, string displayName)
    {
        if (workItemId == Guid.Empty || auditActorId == Guid.Empty || effectiveActorId == Guid.Empty)
            throw new ArgumentException("Trusted submission attribution requires nonempty identities.");
        var previous = _current.Value;
        _current.Value = new(workItemId, auditActorId, effectiveActorId, displayName);
        return new Scope(() => _current.Value = previous);
    }

    public Attribution? Resolve(string? contextType, string? contextId, string? authenticatedActor)
    {
        var current = _current.Value;
        if (current is null || !string.Equals(contextType, "WorkItem", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(contextId?.Split(':')[0], out var item) || item != current.WorkItemId) return null;
        if (!Guid.TryParse(authenticatedActor, out var actor) || actor != current.AuditActorId)
            throw new UnauthorizedAccessException("Trusted submission audit actor does not match the authenticated caller.");
        return current;
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
