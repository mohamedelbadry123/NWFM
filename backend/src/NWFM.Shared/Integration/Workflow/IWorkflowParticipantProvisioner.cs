namespace NWFM.Shared.Integration.Workflow;

public interface IWorkflowParticipantProvisioner
{
    Task<Guid> EnsureParticipantAsync(
        Guid organizationId,
        Guid userId,
        string displayName,
        string? displayNameAr,
        string email,
        CancellationToken cancellationToken = default);
}
