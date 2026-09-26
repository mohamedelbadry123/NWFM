namespace FormEngine.Domain.Constants;

/// <summary>
/// Constraint a <c>date</c> / <c>date_time</c> field may place on its answer, measured against the
/// system clock at the moment the form is filled. Mirrors <c>DATE_RULES</c> in the Angular client
/// (<c>formly-preview.types.ts</c>) — the two must stay in sync, since the same string travels in
/// the form schema JSON and is enforced on both sides.
/// </summary>
/// <remarks>
/// A <c>date</c> is compared at calendar-day precision and a <c>date_time</c> to the minute, so
/// <see cref="After"/> means <em>after today</em> on the one and <em>later than now</em> on the
/// other. The rule may be narrowed further by the field's fixed <c>min_date</c> / <c>max_date</c>
/// bounds, which are whole days at either precision.
/// </remarks>
public static class FormDateRules
{
    /// <summary>No constraint. Also what an unrecognised or absent rule reads as.</summary>
    public const string None = "none";

    /// <summary>Strictly later than the system date/time.</summary>
    public const string After = "after";

    /// <summary>The system date/time, or later.</summary>
    public const string OnOrAfter = "on_or_after";

    /// <summary>Strictly earlier than the system date/time.</summary>
    public const string Before = "before";

    /// <summary>The system date/time, or earlier.</summary>
    public const string OnOrBefore = "on_or_before";

    public static readonly IReadOnlyList<string> All =
    [
        None,
        After,
        OnOrAfter,
        Before,
        OnOrBefore,
    ];

    public static bool IsDefined(string? value) =>
        value is not null && All.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the rule actually constrains anything.</summary>
    public static bool Constrains(string? value) =>
        IsDefined(value) && !string.Equals(value, None, StringComparison.OrdinalIgnoreCase);
}
