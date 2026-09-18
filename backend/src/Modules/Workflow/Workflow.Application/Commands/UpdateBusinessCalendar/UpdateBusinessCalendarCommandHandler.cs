namespace Workflow.Application.Commands.UpdateBusinessCalendar;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class UpdateBusinessCalendarCommandHandler
    : IRequestHandler<UpdateBusinessCalendarCommand, Result<BusinessCalendarDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IBusinessCalendarRepository _repo;

    public UpdateBusinessCalendarCommandHandler(IWorkflowFeatureGate gate, IBusinessCalendarRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<BusinessCalendarDto>> Handle(
        UpdateBusinessCalendarCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<BusinessCalendarDto>(gateResult.Error);

        var calendar = await _repo.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        if (calendar is null)
            return Result.Failure<BusinessCalendarDto>(WorkflowErrors.Calendar.NotFound);

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return Result.Failure<BusinessCalendarDto>(WorkflowErrors.Calendar.InvalidTimeZone);
        }
        catch (InvalidTimeZoneException)
        {
            return Result.Failure<BusinessCalendarDto>(WorkflowErrors.Calendar.InvalidTimeZone);
        }

        var now = DateTime.UtcNow;
        calendar.Update(request.Name, request.NameAr, request.TimeZone, now);
        if (request.IsActive) calendar.Activate(now);
        else calendar.Deactivate(now);

        await _repo.SaveChangesAsync(cancellationToken);
        return Result.Success(WorkflowOpsMappings.ToDto(calendar, includeDetails: true));
    }
}
