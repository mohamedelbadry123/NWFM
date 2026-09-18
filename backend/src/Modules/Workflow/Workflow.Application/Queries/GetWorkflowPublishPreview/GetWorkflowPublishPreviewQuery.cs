namespace Workflow.Application.Queries.GetWorkflowPublishPreview;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowPublishPreviewQuery(Guid VersionId)
    : IRequest<Result<WorkflowPublishPreviewDto>>;
