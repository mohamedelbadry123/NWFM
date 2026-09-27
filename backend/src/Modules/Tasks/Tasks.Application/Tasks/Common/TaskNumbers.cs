using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;

namespace Tasks.Application.Tasks.Common;

/// <summary>
/// Task numbers: the caller's own, or a generated <c>TSK-yyMMdd-XXXX</c>. The suffix is random rather
/// than sequential so two supervisors raising work in the same second do not race for one counter.
/// </summary>
internal static class TaskNumbers
{
    /// <summary>No 0/O or 1/I, so a number read out over the phone is not misheard.</summary>
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private const int SuffixLength = 4;
    private const int MaxAttempts = 10;

    public static async Task<Result<string>> ResolveAsync(
        ITasksDbContext db,
        string? requested,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var number = requested.Trim();
            return await db.Tasks.AnyAsync(t => t.TaskNumber == number, ct)
                ? Result.Failure<string>(TaskErrors.Task.DuplicateNumber(number))
                : Result.Success(number);
        }

        var prefix = $"TSK-{timeProvider.GetUtcNow():yyMMdd}-";

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = prefix + RandomSuffix();

            if (!await db.Tasks.AnyAsync(t => t.TaskNumber == candidate, ct))
            {
                return Result.Success(candidate);
            }
        }

        // Ten collisions in a row means a day with ~1M tasks; the unique index still guards it.
        return Result.Failure<string>(TaskErrors.Task.DuplicateNumber(prefix + "…"));
    }

    private static string RandomSuffix() =>
        string.Create(SuffixLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
        });
}
