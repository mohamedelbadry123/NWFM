namespace Workflow.Application.Queries.ListInstanceTimers;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListInstanceTimersQuery(
    Guid InstanceId,
    Guid OrganizationId,
    bool BypassTenantCheck = false) : IRequest<Result<IReadOnlyList<WorkflowTimerDto>>>;
