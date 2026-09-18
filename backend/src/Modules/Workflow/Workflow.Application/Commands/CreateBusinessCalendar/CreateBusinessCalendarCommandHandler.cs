namespace Workflow.Application.Commands.CreateBusinessCalendar;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class CreateBusinessCalendarCommandHandler
    : IRequestHandler<CreateBusinessCalendarCommand, Result<BusinessCalendarDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IBusinessCalendarRepository _repo;

    public CreateBusinessCalendarCommandHandler(IWorkflowFeatureGate gate, IBusinessCalendarRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<BusinessCalendarDto>> Handle(
        CreateBusinessCalendarCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<BusinessCalendarDto>(gateResult.Error);

        var existing = await _repo.GetByCodeAsync(request.Code, request.OrganizationId, cancellationToken);
        if (existing is not null)
            return Result.Failure<BusinessCalendarDto>(WorkflowErrors.Calendar.DuplicateCode);

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

        var calendar = BusinessCalendar.Create(
            request.Code, request.Name, request.TimeZone, DateTime.UtcNow,
            request.OrganizationId, request.NameAr);

        await _repo.AddAsync(calendar, cancellationToken);
        return Result.Success(WorkflowOpsMappings.ToDto(calendar));
    }
}
