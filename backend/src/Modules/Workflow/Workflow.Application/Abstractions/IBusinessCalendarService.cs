namespace Workflow.Application.Abstractions;

using Workflow.Domain.Enums;

/// <summary>
/// Calculates due timestamps using an optional business calendar (working hours / holidays).
/// </summary>
public interface IBusinessCalendarService
{
    Task<DateTime> CalculateDueAtAsync(
        Guid? businessCalendarId,
        DateTime fromUtc,
        int duration,
        SlaDurationUnit durationUnit,
        CancellationToken cancellationToken = default);
}
