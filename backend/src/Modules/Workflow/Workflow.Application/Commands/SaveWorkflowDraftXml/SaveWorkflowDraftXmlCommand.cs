namespace Workflow.Application.Commands.SaveWorkflowDraftXml;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record SaveWorkflowDraftXmlCommand(
    Guid VersionId,
    string XmlContent,
    string? DesignerJson = null) : IRequest<Result<WorkflowVersionDto>>;
