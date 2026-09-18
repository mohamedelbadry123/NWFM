namespace Workflow.Application.Commands.CreateBusinessCalendar;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record CreateBusinessCalendarCommand(
    string Code,
    string Name,
    string TimeZone,
    string? NameAr = null,
    Guid? OrganizationId = null) : IRequest<Result<BusinessCalendarDto>>;
