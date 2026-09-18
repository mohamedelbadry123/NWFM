namespace Workflow.Application.Queries.GetWorkflowVersionXml;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowVersionXmlQueryHandler
    : IRequestHandler<GetWorkflowVersionXmlQuery, Result<string>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowVersionRepository _versionRepo;

    public GetWorkflowVersionXmlQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowVersionRepository versionRepo)
    {
        _gate = gate;
        _versionRepo = versionRepo;
    }

    public async Task<Result<string>> Handle(
        GetWorkflowVersionXmlQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<string>(gateResult.Error);

        var version = await _versionRepo.GetByIdAsync(
            request.VersionId, cancellationToken);
        if (version is null)
            return Result.Failure<string>(WorkflowErrors.Version.NotFound);

        return Result.Success(version.XmlContent);
    }
}
