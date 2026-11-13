namespace FormEngine.Domain.Entities;

/// <summary>Global receipt-key reservation committed in the same transaction as a workflow form fill.
/// Contains no answers or actor data; survives a rollback of the separate Workflow transaction.</summary>
public sealed class WorkflowReceiptKey
{
    public Guid ClientSubmissionId { get; private set; }
    private WorkflowReceiptKey() { }
    public WorkflowReceiptKey(Guid key) => ClientSubmissionId = key;
}
