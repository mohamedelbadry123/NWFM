namespace Workflow.Application.Commands.RetireWorkflowVersion;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record RetireWorkflowVersionCommand(
    Guid VersionId) : IRequest<Result<WorkflowVersionDto>>;
