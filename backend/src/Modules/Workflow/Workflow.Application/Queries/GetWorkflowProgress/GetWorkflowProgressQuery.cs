namespace Workflow.Application.Queries.GetWorkflowProgress;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowProgressQuery(Guid InstanceId, Guid OrganizationId)
    : IRequest<Result<WorkflowProgressDto>>;
