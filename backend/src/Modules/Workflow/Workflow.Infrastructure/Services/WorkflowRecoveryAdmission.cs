namespace Workflow.Infrastructure.Services;

/// <summary>Only failures of the tenant-admission callback terminate a recovery batch.</summary>
internal sealed class WorkflowTenantAdmissionRefusedException(string message, Exception inner)
    : UnauthorizedAccessException(message, inner);

internal static class WorkflowRecoveryAdmission
{
    internal static async Task RequireAsync(Func<Guid, CancellationToken, Task>? admit, Guid tenant, CancellationToken ct)
    {
        if (admit is null) return;
        try { await admit(tenant, ct); }
        catch (UnauthorizedAccessException error) when (error is not WorkflowTenantAdmissionRefusedException)
        { throw new WorkflowTenantAdmissionRefusedException("Tenant admission refused recovery.", error); }
    }
}
