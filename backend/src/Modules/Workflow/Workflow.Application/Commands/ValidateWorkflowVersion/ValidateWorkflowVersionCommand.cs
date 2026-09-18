namespace Workflow.Application.Commands.ValidateWorkflowVersion;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ValidateWorkflowVersionCommand(
    Guid VersionId) : IRequest<Result<WorkflowValidationResultDto>>;
