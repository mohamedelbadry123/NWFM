namespace NWFM.Shared.Integration.Workflow;

public sealed record WorkflowUserInfo(
    Guid UserId,
    string DisplayName,
    string? DisplayNameAr,
    string Email);
