namespace Workflow.Application.Queries.GetWorkflowBindingById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowBindingByIdQuery(Guid BindingId) : IRequest<Result<WorkflowBindingDto>>;
