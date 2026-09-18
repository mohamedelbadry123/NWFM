namespace Workflow.Application.Commands.AddBusinessCalendarHoliday;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class AddBusinessCalendarHolidayCommandHandler
    : IRequestHandler<AddBusinessCalendarHolidayCommand, Result<BusinessCalendarHolidayDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IBusinessCalendarRepository _repo;

    public AddBusinessCalendarHolidayCommandHandler(IWorkflowFeatureGate gate, IBusinessCalendarRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<BusinessCalendarHolidayDto>> Handle(
        AddBusinessCalendarHolidayCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<BusinessCalendarHolidayDto>(gateResult.Error);

        var calendar = await _repo.GetByIdWithDetailsAsync(request.CalendarId, cancellationToken);
        if (calendar is null)
            return Result.Failure<BusinessCalendarHolidayDto>(WorkflowErrors.Calendar.NotFound);

        var holiday = calendar.AddHoliday(
            request.HolidayDate, request.Name, DateTime.UtcNow,
            request.NameAr, request.IsRecurring);

        await _repo.SaveChangesAsync(cancellationToken);
        return Result.Success(WorkflowOpsMappings.ToDto(holiday));
    }
}
