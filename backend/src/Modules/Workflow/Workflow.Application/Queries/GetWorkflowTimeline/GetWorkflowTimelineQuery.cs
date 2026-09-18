namespace Workflow.Application.Queries.GetWorkflowTimeline;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowTimelineQuery(
    Guid InstanceId,
    Guid OrganizationId) : IRequest<Result<IReadOnlyList<WorkflowEventDto>>>;
