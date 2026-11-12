using System.Text.Json;
using Workflow.Domain.Entities;

namespace Workflow.Application.Mapping;

public sealed record StepMappingValue(string BindingId, string FormStepId, Guid FormId, int FormVersion,
    string FieldKey, string Mode, JsonElement Value);
public sealed record StepMappingSnapshot(Guid InvocationId, Guid VisitId, string Status,
    IReadOnlyList<StepMappingValue> Values, IReadOnlyList<ResolvedMappingSource> Provenance);
public interface IStepMappingRuntime
{
    Task PrepareStepAsync(WorkItem item, string configurationJson, CancellationToken ct);
    Task<StepMappingSnapshot?> ReadStepAsync(WorkItem item, Guid visitId, CancellationToken ct);
}
