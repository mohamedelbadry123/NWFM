namespace Workflow.Application.Queries.GetWorkflowIncidentById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowIncidentByIdQuery(Guid Id) : IRequest<Result<WorkflowIncidentDto>>;
