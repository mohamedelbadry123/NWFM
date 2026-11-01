using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Provisioning;

/// <summary>
/// Provisioning runs on the platform work queue. A run lost with a stopped node is safe: the
/// checkpoints let the sweep, or a platform admin's retry, resume from the last completed step.
/// </summary>
internal sealed class SignupProvisioningQueue(IPlatformWorkQueue queue, ILogger<SignupProvisioningQueue> logger)
    : ISignupProvisioningQueue
{
    public void Enqueue(Guid signupRequestId)
    {
        if (!queue.Enqueue($"signup:{signupRequestId:N}",
                (services, ct) => services.GetRequiredService<ISignupProvisioner>().ProvisionAsync(signupRequestId, ct)))
            logger.LogWarning("The platform work queue refused signup {SignupId}; the sweep will pick it up.", signupRequestId);
    }
}
