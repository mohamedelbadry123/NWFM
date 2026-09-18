namespace Workflow.Application.Queries.GetWorkflowBindingReadiness;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowBindingReadinessQuery(Guid BindingId)
    : IRequest<Result<WorkflowBindingReadinessDto>>;
