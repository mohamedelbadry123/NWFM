namespace FormEngine.Application.Common.Schema;

/// <summary>
/// Element <c>type</c> values used by the form-builder schema JSON. Mirrors
/// <c>ELEMENT_TYPES</c> in the Angular builder (<c>form-builder.types.ts</c>) — the two must stay
/// in sync, since the same string is what a field's SQL column type is derived from.
/// </summary>
public static class FormElementTypes
{
    public const string Text = "text";

    /// <summary>
    /// Long free text — comments, remarks, a memo. Stored the same way as <see cref="Text"/>
    /// (<c>NVARCHAR(MAX)</c>); the builder renders it as a textarea.
    /// </summary>
    public const string Memo = "memo";

    public const string Numeric = "numeric";
    public const string YesNo = "yes_no";
    public const string Date = "date";
    public const string Time = "time";

    /// <summary>
    /// A from/to time pair per weekday — the paper form's <c>ساعات العمل</c> row. The answer is a
    /// JSON object keyed by day code; see <see cref="FormCalendarWithHours"/>.
    /// </summary>
    public const string CalendarWithHours = "calendar_with_hours";

    /// <summary>
    /// A calendar date and a clock time answered together, stored as the local
    /// <c>YYYY-MM-DD HH:mm</c> the web client writes.
    /// </summary>
    public const string DateTime = "date_time";

    public const string SingleChoice = "single_choice";
    public const string MultipleChoice = "multiple_choice";
    public const string Signature = "signature";
    public const string Photo = "photo";
    public const string Video = "video";
    public const string Audio = "audio";
    public const string Geolocation = "geolocation";

    /// <summary>Any document the user attaches — a permit PDF, a signed work order, a spreadsheet.</summary>
    public const string File = "file";

    /// <summary>
    /// A code read off an asset — a meter serial, a valve tag. The answer is the decoded text,
    /// whatever symbology it was scanned from, so it stores exactly like a short text answer.
    /// </summary>
    public const string Barcode = "barcode";

    public const string Section = "section";

    /// <summary>
    /// Types whose value is a JSON array of uploaded-file references
    /// (<c>fileId</c> + <c>path</c>), not the content itself.
    /// </summary>
    public static readonly IReadOnlyList<string> Media =
    [
        Photo,
        Video,
        Audio,
        Signature,
        File,
    ];

    public static bool IsMedia(string? value) =>
        value is not null && Media.Contains(value, StringComparer.OrdinalIgnoreCase);
}
