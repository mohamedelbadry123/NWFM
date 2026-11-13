using System.Text.Json;
using NWFM.Shared.Results;

namespace Workflow.Application.Mapping;

public sealed record MappingInvocationDto(Guid Id, Guid RootInstanceId, Guid OriginActivityId, Guid? OriginTokenId,
    Guid TargetInstanceId, Guid TargetVersionId, string TargetNodeKey, string ExecutionKind, Guid? ParentInvocationId,
    string ActionId, string Status, DateTime WindowAnchor, DateTime? Deadline, bool Prepared, bool Delivered,
    bool Applied, bool GraphAdvanced, string? Error, DateTime? CompletedAt, Guid? OperationId = null);
public interface IMappingInvocations
{
    Task<Result<PreparedMappingInputsDto>> ReadPreparedInputsAsync(Guid invocationId, Guid actorId, CancellationToken ct);
    Task<Result<IReadOnlyList<MappingInvocationDto>>> ListAsync(Guid rootInstanceId, Guid actorId, CancellationToken ct);
    Task<Result> RetryAsync(Guid invocationId, Guid actorId, CancellationToken ct);
}
public sealed record PreparedMappingInputsDto(Guid InvocationId, Guid RootInstanceId, Guid TargetInstanceId,
    CallableTarget Target, Guid? ParentInvocationId, string ActionId, JsonElement Inputs, IReadOnlyList<ResolvedMappingSource> Provenance);
