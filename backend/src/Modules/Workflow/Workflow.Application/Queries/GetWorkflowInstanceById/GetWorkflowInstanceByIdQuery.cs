namespace Workflow.Application.Queries.GetWorkflowInstanceById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowInstanceByIdQuery(Guid InstanceId, Guid OrganizationId)
    : IRequest<Result<WorkflowInstanceDto>>;
