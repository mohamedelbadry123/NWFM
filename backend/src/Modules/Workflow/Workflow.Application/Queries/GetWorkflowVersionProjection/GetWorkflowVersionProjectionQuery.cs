namespace Workflow.Application.Queries.GetWorkflowVersionProjection;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowVersionProjectionQuery(
    Guid VersionId) : IRequest<Result<WorkflowVersionDetailDto>>;
