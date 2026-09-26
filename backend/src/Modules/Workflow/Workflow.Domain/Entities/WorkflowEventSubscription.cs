namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

public sealed class WorkflowEventSubscription : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public Guid ActivityInstanceId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string EventKey { get; private set; } = "";
    public string CorrelationId { get; private set; } = "";
    public string ConfigurationJson { get; private set; } = "{}";
    public DateTime ExpiresAt { get; private set; }
    public string Status { get; private set; } = "Waiting";
    public byte[] RowVersion { get; private set; } = [];
    private WorkflowEventSubscription() { }
    public static WorkflowEventSubscription Create(Guid org, Guid instance, Guid activity, Guid connection,
        string eventKey, string correlation, string config, DateTime expires) => new()
    { Id = Guid.NewGuid(), OrganizationId = org, WorkflowInstanceId = instance, ActivityInstanceId = activity,
      ConnectionId = connection, EventKey = eventKey, CorrelationId = correlation, ConfigurationJson = config,
      ExpiresAt = expires, CreatedAt = DateTime.UtcNow };
    public void Finish(string status) { Status = status; SetUpdated(DateTime.UtcNow); }
}

public sealed class WorkflowEventReceipt : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string EventId { get; private set; } = "";
    public string EventKey { get; private set; } = "";
    public string CorrelationId { get; private set; } = "";
    public string PayloadJson { get; private set; } = "{}";
    public string Status { get; private set; } = "Received";
    public Guid? ActivityInstanceId { get; private set; }
    public string? Error { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    private WorkflowEventReceipt() { }
    public static WorkflowEventReceipt Create(Guid org, Guid connection, string eventId, string eventKey, string correlation, string payload) => new()
    { Id = Guid.NewGuid(), OrganizationId = org, ConnectionId = connection, EventId = eventId,
      EventKey = eventKey, CorrelationId = correlation, PayloadJson = payload, CreatedAt = DateTime.UtcNow };
    public void Finish(string status, Guid? activity = null, string? error = null)
    { Status = status; ActivityInstanceId = activity; Error = error; SetUpdated(DateTime.UtcNow); }
}
