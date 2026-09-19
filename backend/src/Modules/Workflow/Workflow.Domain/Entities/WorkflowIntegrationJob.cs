namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

public sealed class WorkflowIntegrationJob : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public Guid ActivityInstanceId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string Kind { get; private set; } = "Http";
    public string ConfigurationJson { get; private set; } = "{}";
    public string InputJson { get; private set; } = "{}";
    public string OperationKey { get; private set; } = "";
    public string Status { get; private set; } = "Pending";
    public int Attempts { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public DateTime? LeaseUntil { get; private set; }
    public Guid? LeaseOwner { get; private set; }
    public string? Error { get; private set; }
    public int? StatusCode { get; private set; }
    public string? ResultJson { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    private WorkflowIntegrationJob() { }
    public static WorkflowIntegrationJob Create(Guid org, Guid instance, Guid activity, Guid connection,
        string kind, string configuration, string input, string operationKey) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = org, WorkflowInstanceId = instance, ActivityInstanceId = activity,
        ConnectionId = connection, Kind = kind, ConfigurationJson = configuration, InputJson = input,
        OperationKey = operationKey, CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow
    };
    public void Claim(Guid owner, DateTime until) { Status = "Running"; Attempts++; LeaseOwner = owner; LeaseUntil = until; }
    public void RecordDelivery(string result, int? statusCode)
    { Status = "Delivered"; ResultJson = result; StatusCode = statusCode; LeaseOwner = null; LeaseUntil = null; SetUpdated(DateTime.UtcNow); }
    public void Complete(string result, int? statusCode)
    { Status = "Completed"; ResultJson = result; StatusCode = statusCode; Error = null; LeaseOwner = null; LeaseUntil = null; SetUpdated(DateTime.UtcNow); }
    public void Fail(string error, int? statusCode, DateTime? retryAt)
    { Error = error; StatusCode = statusCode; Status = retryAt.HasValue ? "Pending" : "Failed"; NextAttemptAt = retryAt ?? NextAttemptAt; LeaseOwner = null; LeaseUntil = null; SetUpdated(DateTime.UtcNow); }
    public void Replay() { Status = "Pending"; Attempts = 0; Error = null; NextAttemptAt = DateTime.UtcNow; LeaseUntil = null; LeaseOwner = null; }
    public void Cancel() { Status = "Cancelled"; LeaseOwner = null; LeaseUntil = null; SetUpdated(DateTime.UtcNow); }
}
