namespace Workflow.Application.Queries.GetAssignmentGroupById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetAssignmentGroupByIdQuery(
    Guid GroupId,
    Guid OrganizationId) : IRequest<Result<WorkflowAssignmentGroupDetailDto>>;
