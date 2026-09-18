namespace Workflow.Application.Queries.GetWorkflowRequestById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowRequestByIdQuery(Guid RequestId, Guid OrganizationId)
    : IRequest<Result<WorkflowRequestDto>>;

public sealed record GetWorkflowRequestByInstanceIdQuery(Guid InstanceId, Guid OrganizationId)
    : IRequest<Result<WorkflowRequestDto>>;
