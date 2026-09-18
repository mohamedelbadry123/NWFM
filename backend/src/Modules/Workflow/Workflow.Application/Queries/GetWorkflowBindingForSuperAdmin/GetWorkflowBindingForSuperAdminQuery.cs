namespace Workflow.Application.Queries.GetWorkflowBindingForSuperAdmin;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowBindingForSuperAdminQuery(Guid BindingId)
    : IRequest<Result<WorkflowBindingDto>>;
