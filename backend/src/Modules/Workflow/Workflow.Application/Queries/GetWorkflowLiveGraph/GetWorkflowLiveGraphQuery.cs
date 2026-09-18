namespace Workflow.Application.Queries.GetWorkflowLiveGraph;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowLiveGraphQuery(
    Guid InstanceId,
    Guid OrganizationId,
    bool SuperAdmin)
    : IRequest<Result<WorkflowLiveGraphDto>>;
