using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Application.Mapping;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowMappingRuntime : IMappingInvocations
{
    public async Task<Result<PreparedMappingInputsDto>> ReadPreparedInputsAsync(Guid invocationId, Guid actorId, CancellationToken ct)
    {
        var invocation = await db.MappingInvocations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == invocationId, ct);
        if (invocation is not { ExecutionKind: "MapOnly", Applied: true } || invocation.Status == "Cancelled" || invocation.InvocationInputsJson is null)
            return Result.Failure<PreparedMappingInputsDto>(new("Mapping.NotPrepared", "Prepared inputs are unavailable."));
        try
        {
            var root = await db.WorkflowInstances.SingleAsync(i => i.Id == invocation.RootInstanceId, ct);
            var target = await db.WorkflowInstances.SingleAsync(i => i.Id == invocation.TargetInstanceId, ct);
            await resolver.AuthorizeAsync(root, actorId, false, ct); await resolver.AuthorizeAsync(target, actorId, false, ct);
            var provenance = await AuthorizeProvenanceAsync(Read<List<ResolvedMappingSource>>(invocation.SourcesJson), root.OrganizationId, actorId, ct);
            var version = await db.WorkflowVersions.SingleAsync(v => v.Id == invocation.TargetVersionId, ct);
            return Result.Success(new PreparedMappingInputsDto(invocation.Id, root.Id, target.Id,
                new(version.WorkflowDefinitionId, version.Id, invocation.TargetNodeKey), invocation.ParentInvocationId, invocation.ActionId,
                Read<System.Text.Json.JsonElement>(invocation.InvocationInputsJson), provenance.DistinctBy(p => p.Source).ToList()));
        }
        catch (UnauthorizedAccessException) { return Result.Failure<PreparedMappingInputsDto>(new("Mapping.Forbidden", "Prepared inputs are unavailable.")); }
    }
    public async Task<Result<IReadOnlyList<MappingInvocationDto>>> ListAsync(Guid rootInstanceId, Guid actorId, CancellationToken ct)
    {
        var root = await db.WorkflowInstances.SingleOrDefaultAsync(i => i.Id == rootInstanceId, ct);
        if (root is null) return Result.Failure<IReadOnlyList<MappingInvocationDto>>(new("Mapping.NotFound", "Workflow is unavailable."));
        // A child instance (e.g. a work item's workflow) lists its tree's receipts: climb to the real root, which is then authorized.
        for (var depth = 0; root.ParentInstanceId is Guid parentId && depth < 32; depth++)
        {
            root = await db.WorkflowInstances.SingleOrDefaultAsync(i => i.Id == parentId, ct);
            if (root is null) return Result.Failure<IReadOnlyList<MappingInvocationDto>>(new("Mapping.NotFound", "Workflow is unavailable."));
        }
        rootInstanceId = root.Id;
        try { await resolver.AuthorizeAsync(root, actorId, false, ct); }
        catch (UnauthorizedAccessException) { return Result.Failure<IReadOnlyList<MappingInvocationDto>>(new("Mapping.Forbidden", "Workflow is unavailable.")); }
        var rows = await db.MappingInvocations.AsNoTracking().Where(i => i.RootInstanceId == rootInstanceId).OrderByDescending(i => i.CreatedAt).Take(500).ToListAsync(ct);
        // The integration operation (job) delivering an invocation: retry is refused once one exists, replay is the supported path.
        var ids = rows.Select(i => i.Id).ToList();
        var operations = (await db.IntegrationJobs.AsNoTracking().Where(j => j.InvocationId != null && ids.Contains(j.InvocationId.Value)).Select(j => new { Invocation = j.InvocationId!.Value, j.Id }).ToListAsync(ct))
            .ToDictionary(j => j.Invocation, j => j.Id);
        return Result.Success<IReadOnlyList<MappingInvocationDto>>(rows.Select(i => new MappingInvocationDto(i.Id, i.RootInstanceId, i.OriginActivityId, i.OriginTokenId,
            i.TargetInstanceId, i.TargetVersionId, i.TargetNodeKey, i.ExecutionKind, i.ParentInvocationId, i.ActionId, i.Status, i.WindowAnchor,
            i.Deadline, i.PreparedRequestJson is not null || i.ExecutionKind == "MapOnly" && i.Applied, i.ResponseJson is not null, i.Applied, i.GraphAdvanced, i.Error, i.CompletedAt,
            operations.TryGetValue(i.Id, out var operationId) ? operationId : null)).ToList());
    }
    public async Task<Result> RetryAsync(Guid invocationId, Guid actorId, CancellationToken ct)
    {
        var invocation = await db.MappingInvocations.SingleOrDefaultAsync(i => i.Id == invocationId, ct);
        if (invocation is null) return Result.Failure(new("Mapping.NotFound", "Invocation is unavailable."));
        return await WorkflowExecutionLock.RunAsync(db, "instance:" + invocation.RootInstanceId, async () =>
        {
            await db.Entry(invocation).ReloadAsync(ct);
            var allowed = await ListAsync(invocation.RootInstanceId, actorId, ct);
            if (allowed.IsFailure) return Result.Failure(allowed.Error);
            if (invocation.Status != "Attention" || invocation.ExecutionKind == "Graph" && invocation.GraphAdvanced || !await RunnableAsync(invocation, ct)) return Result.Failure(new("Mapping.NotReplayable", "Only runnable invocations needing attention whose graph has not advanced can be retried."));
            var job = await db.IntegrationJobs.SingleOrDefaultAsync(j => j.InvocationId == invocation.Id, ct);
            if (job is not null) return Result.Failure(new("Mapping.OperationReplay", "Replay the associated integration operation; successful deliveries are reapplied without resending."));
            invocation.Retry(); await db.SaveChangesAsync(ct); return Result.Success();
        }, ct);
    }
}
