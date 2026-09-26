namespace FormEngine.Domain.Options;

/// <summary>
/// Binds the <c>FormEngine</c> configuration section.
/// </summary>
public sealed class FormEngineOptions
{
    public const string SectionName = "FormEngine";

    public const string DefaultTimeZoneId = "Asia/Riyadh";

    /// <summary>
    /// The operating time zone. A date rule such as "on or before today" is judged on the calendar
    /// of the people filling the form, not the server's UTC clock — so a fill at 01:00 local time is
    /// not rejected as tomorrow, or accepted as yesterday.
    /// </summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    /// <summary>
    /// When true, answers are checked against the form's required, length, pattern, numeric and
    /// choice rules on submit. Date rules are always enforced, whatever this says.
    /// </summary>
    public bool EnforceFieldRules { get; set; } = true;

    /// <summary>Resolves <see cref="TimeZoneId"/>, falling back to UTC when the id is unknown.</summary>
    public TimeZoneInfo ResolveTimeZone()
    {
        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(TimeZoneId.Trim(), out var zone)
            ? zone
            : TimeZoneInfo.Utc;
    }
}
