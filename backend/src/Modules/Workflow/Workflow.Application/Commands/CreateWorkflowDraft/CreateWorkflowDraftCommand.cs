namespace Workflow.Application.Commands.CreateWorkflowDraft;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record CreateWorkflowDraftCommand(
    Guid DefinitionId,
    Guid CreatedByUserId,
    string? ChangeSummary = null) : IRequest<Result<WorkflowVersionDto>>;
