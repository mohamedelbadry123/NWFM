namespace Workflow.Application.Commands.CreateSlaPolicy;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class CreateSlaPolicyCommandHandler
    : IRequestHandler<CreateSlaPolicyCommand, Result<SlaPolicyDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly ISlaPolicyRepository _repo;
    private readonly IBusinessCalendarRepository _calendars;

    public CreateSlaPolicyCommandHandler(
        IWorkflowFeatureGate gate, ISlaPolicyRepository repo, IBusinessCalendarRepository calendars)
    {
        _gate = gate;
        _repo = repo;
        _calendars = calendars;
    }

    public async Task<Result<SlaPolicyDto>> Handle(
        CreateSlaPolicyCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<SlaPolicyDto>(gateResult.Error);

        var calendar = await _calendars.GetByIdAsync(request.BusinessCalendarId, cancellationToken);
        if (calendar is null)
            return Result.Failure<SlaPolicyDto>(WorkflowErrors.Sla.CalendarRequired);

        var existing = await _repo.GetByCodeAsync(request.PolicyCode, request.OrganizationId, cancellationToken);
        if (existing is not null)
            return Result.Failure<SlaPolicyDto>(WorkflowErrors.Sla.DuplicateCode);

        var policy = SlaPolicy.Create(
            request.PolicyCode, request.Name, request.Duration, request.DurationUnit,
            request.BusinessCalendarId, DateTime.UtcNow, request.OrganizationId, request.NameAr,
            request.ReminderThresholdsJson, request.EscalationThresholdsJson, request.EscalationAssignmentKey);

        await _repo.AddAsync(policy, cancellationToken);
        return Result.Success(WorkflowOpsMappings.ToDto(policy));
    }
}
