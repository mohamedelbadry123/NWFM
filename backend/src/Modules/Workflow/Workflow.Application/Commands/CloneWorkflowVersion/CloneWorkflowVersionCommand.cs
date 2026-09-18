namespace Workflow.Application.Commands.CloneWorkflowVersion;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record CloneWorkflowVersionCommand(
    Guid SourceVersionId,
    Guid CreatedByUserId,
    string? ChangeSummary = null) : IRequest<Result<WorkflowVersionDto>>;
