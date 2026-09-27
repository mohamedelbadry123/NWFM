namespace Workflow.Application.Commands.RegisterParticipant;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record RegisterParticipantCommand(
    Guid OrganizationId,
    string DisplayName,
    string Email,
    string? DisplayNameAr,
    string? EmployeeNumber,
    Guid? UserId = null) : IRequest<Result<WorkflowParticipantDto>>;
