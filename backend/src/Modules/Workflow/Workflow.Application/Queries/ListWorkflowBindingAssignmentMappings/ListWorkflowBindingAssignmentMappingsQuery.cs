namespace Workflow.Application.Queries.ListWorkflowBindingAssignmentMappings;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListWorkflowBindingAssignmentMappingsQuery(
    Guid BindingId,
    Guid OrganizationId) : IRequest<Result<List<WorkflowBindingAssignmentMappingDto>>>;
