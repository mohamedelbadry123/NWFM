namespace Workflow.Application.Commands.UpdateBusinessCalendar;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record UpdateBusinessCalendarCommand(
    Guid Id,
    string Name,
    string? NameAr,
    string TimeZone,
    bool IsActive) : IRequest<Result<BusinessCalendarDto>>;
