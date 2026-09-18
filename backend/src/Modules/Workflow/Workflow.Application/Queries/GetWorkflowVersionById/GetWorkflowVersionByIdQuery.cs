namespace Workflow.Application.Queries.GetWorkflowVersionById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowVersionByIdQuery(
    Guid VersionId) : IRequest<Result<WorkflowVersionDetailDto>>;
