namespace Workflow.Application.Queries.ListActiveGroupsForOrg;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListActiveGroupsForOrgQuery(Guid OrganizationId)
    : IRequest<Result<IReadOnlyList<WorkflowAssignmentGroupDto>>>;
