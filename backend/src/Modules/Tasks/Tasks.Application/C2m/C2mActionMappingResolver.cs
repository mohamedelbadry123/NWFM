using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Options;
using Tasks.Application.Common.Interfaces;
using Tasks.Domain.Constants;

namespace Tasks.Application.C2m;

/// <summary>Reads the <c>Action Taken</c> → C2M outcome for one answer.</summary>
public interface IC2mActionMappingResolver
{
    Task<C2mActionOutcome> ResolveAsync(string? actionCode, IReadOnlyList<FormFieldInfo> fields, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves an <c>Action Taken</c> code: the answered option on the form first, then the C2M action
/// mapping table (cached whole, evicted on any write), then the built-in rule.
/// </summary>
/// <remarks>
/// A code with no option status and no active mapping closes as cancelled, carrying itself as the
/// reason: reporting work as done when it cannot be accounted for is the worse of the two errors, so
/// an empty mapping degrades to the safe answer rather than to no answer.
/// </remarks>
public sealed class C2mActionMappingResolver(
    ITasksDbContext db,
    ICacheService cache,
    IOptions<CacheSettings> cacheSettings)
    : IC2mActionMappingResolver
{
    public async Task<C2mActionOutcome> ResolveAsync(
        string? actionCode,
        IReadOnlyList<FormFieldInfo> fields,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actionCode))
        {
            return Fallback(actionCode);
        }

        if (FromChoice(fields, actionCode) is { } fromChoice)
        {
            return fromChoice;
        }

        var mappings = await cache.GetOrCreateAsync(
            CacheKeys.Tasks.C2mActionMappings,
            LoadAsync,
            cacheSettings.Value.ToLookupEntryOptions(),
            cancellationToken);

        if (!mappings.TryGetValue(Normalize(actionCode), out var mapping))
        {
            return Fallback(actionCode);
        }

        // C2M rejects the reason that does not belong to the status, so only the matching one travels.
        return mapping.FaStatus == C2mOperationStatuses.Completed
            ? new C2mActionOutcome(C2mOperationStatuses.Completed, CancelReason: null, mapping.ClosureReason)
            : new C2mActionOutcome(C2mOperationStatuses.Cancelled, mapping.CancelReason ?? actionCode.Trim(), ClosureReason: null);
    }

    /// <summary>The option the crew picked on the Action Taken field, when that option names a C2M status itself.</summary>
    private static C2mActionOutcome? FromChoice(IReadOnlyList<FormFieldInfo> fields, string actionCode)
    {
        var field = fields.FirstOrDefault(f =>
            string.Equals(f.DataName, C2mFieldNames.ActionTaken, StringComparison.OrdinalIgnoreCase));

        var normalized = Normalize(actionCode);
        var choice = field?.Choices.FirstOrDefault(c => Normalize(c.Value) == normalized);

        if (choice is null || !C2mOperationStatuses.IsDefined(choice.C2mFaStatus))
        {
            return null;
        }

        var reason = string.IsNullOrWhiteSpace(choice.C2mReason) ? null : choice.C2mReason.Trim();

        return choice.C2mFaStatus == C2mOperationStatuses.Completed
            ? new C2mActionOutcome(C2mOperationStatuses.Completed, CancelReason: null, reason)
            : new C2mActionOutcome(C2mOperationStatuses.Cancelled, reason ?? actionCode.Trim(), ClosureReason: null);
    }

    private static C2mActionOutcome Fallback(string? actionCode) =>
        new(
            C2mOperationStatuses.StatusForAction(actionCode),
            C2mOperationStatuses.CancelReasonForAction(actionCode),
            ClosureReason: null);

    private async ValueTask<Dictionary<string, C2mActionMappingEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        var rows = await db.C2mActionMappings
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.ActionCode, x.FaStatus, x.CancelReason, x.ClosureReason })
            .ToListAsync(cancellationToken);

        // Normalised keys rather than a case-insensitive comparer: the cache round-trips through JSON,
        // and a comparer does not survive it.
        var byCode = new Dictionary<string, C2mActionMappingEntry>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            byCode[Normalize(row.ActionCode)] = new C2mActionMappingEntry(row.FaStatus, row.CancelReason, row.ClosureReason);
        }

        return byCode;
    }

    private static string Normalize(string code) => code.Trim().ToUpperInvariant();
}

/// <summary>The cached shape — a record, so it survives the cache's JSON round trip.</summary>
public sealed record C2mActionMappingEntry(string FaStatus, string? CancelReason, string? ClosureReason);
