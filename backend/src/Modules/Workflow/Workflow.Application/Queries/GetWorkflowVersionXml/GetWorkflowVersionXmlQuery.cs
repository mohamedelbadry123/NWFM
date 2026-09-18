namespace Workflow.Application.Queries.GetWorkflowVersionXml;

using MediatR;
using NWFM.Shared.Results;

public sealed record GetWorkflowVersionXmlQuery(
    Guid VersionId) : IRequest<Result<string>>;
