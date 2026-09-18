namespace Workflow.Application.Commands.UpdateSlaPolicy;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class UpdateSlaPolicyCommandHandler
    : IRequestHandler<UpdateSlaPolicyCommand, Result<SlaPolicyDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly ISlaPolicyRepository _repo;
    private readonly IBusinessCalendarRepository _calendars;

    public UpdateSlaPolicyCommandHandler(
        IWorkflowFeatureGate gate, ISlaPolicyRepository repo, IBusinessCalendarRepository calendars)
    {
        _gate = gate;
        _repo = repo;
        _calendars = calendars;
    }

    public async Task<Result<SlaPolicyDto>> Handle(
        UpdateSlaPolicyCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<SlaPolicyDto>(gateResult.Error);

        var policy = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (policy is null)
            return Result.Failure<SlaPolicyDto>(WorkflowErrors.Sla.NotFound);

        var calendar = await _calendars.GetByIdAsync(request.BusinessCalendarId, cancellationToken);
        if (calendar is null)
            return Result.Failure<SlaPolicyDto>(WorkflowErrors.Sla.CalendarRequired);

        var now = DateTime.UtcNow;
        policy.Update(
            request.Name, request.NameAr, request.Duration, request.DurationUnit,
            request.BusinessCalendarId, request.ReminderThresholdsJson,
            request.EscalationThresholdsJson, request.EscalationAssignmentKey, now);

        if (request.IsActive) policy.Activate(now);
        else policy.Deactivate(now);

        await _repo.SaveChangesAsync(cancellationToken);
        return Result.Success(WorkflowOpsMappings.ToDto(policy));
    }
}
