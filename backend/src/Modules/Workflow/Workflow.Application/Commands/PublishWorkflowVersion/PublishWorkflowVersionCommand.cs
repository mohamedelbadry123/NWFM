namespace Workflow.Application.Commands.PublishWorkflowVersion;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record PublishWorkflowVersionCommand(
    Guid VersionId,
    Guid PublishedByUserId) : IRequest<Result<WorkflowVersionDto>>;
