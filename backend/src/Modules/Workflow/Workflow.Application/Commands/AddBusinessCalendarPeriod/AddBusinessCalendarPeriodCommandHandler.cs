namespace Workflow.Application.Commands.AddBusinessCalendarPeriod;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class AddBusinessCalendarPeriodCommandHandler
    : IRequestHandler<AddBusinessCalendarPeriodCommand, Result<BusinessCalendarPeriodDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IBusinessCalendarRepository _repo;

    public AddBusinessCalendarPeriodCommandHandler(IWorkflowFeatureGate gate, IBusinessCalendarRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<BusinessCalendarPeriodDto>> Handle(
        AddBusinessCalendarPeriodCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<BusinessCalendarPeriodDto>(gateResult.Error);

        var calendar = await _repo.GetByIdWithDetailsAsync(request.CalendarId, cancellationToken);
        if (calendar is null)
            return Result.Failure<BusinessCalendarPeriodDto>(WorkflowErrors.Calendar.NotFound);
        if (!Enum.IsDefined(request.DayOfWeek) || request.StartTime < TimeSpan.Zero || request.EndTime > TimeSpan.FromDays(1) || request.EndTime <= request.StartTime
            || calendar.Periods.Any(p => p.DayOfWeek == request.DayOfWeek && p.StartTime < request.EndTime && p.EndTime > request.StartTime))
            return Result.Failure<BusinessCalendarPeriodDto>(new Error("Calendar.InvalidPeriod", "Working periods must be positive, within one day, and must not overlap."));

        var period = calendar.AddPeriod(
            request.DayOfWeek, request.StartTime, request.EndTime,
            DateTime.UtcNow, request.IsWorkingTime);

        await _repo.SaveChangesAsync(cancellationToken);
        return Result.Success(WorkflowOpsMappings.ToDto(period));
    }
}
