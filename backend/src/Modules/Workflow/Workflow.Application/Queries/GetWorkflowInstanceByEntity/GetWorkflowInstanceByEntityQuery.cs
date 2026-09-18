namespace Workflow.Application.Queries.GetWorkflowInstanceByEntity;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowInstanceByEntityQuery(
    Guid OrganizationId,
    string ModuleKey,
    string EntityType,
    string EntityId) : IRequest<Result<WorkflowInstanceDto?>>;
