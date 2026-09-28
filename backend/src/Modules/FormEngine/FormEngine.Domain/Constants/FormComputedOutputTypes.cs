namespace FormEngine.Domain.Constants;

/// <summary>
/// What a form's computed column yields. Mirrors <c>COMPUTED_OUTPUT_TYPES</c> in the Angular builder
/// (<c>form-schema.types.ts</c>) — the value travels in the form schema JSON.
/// </summary>
public static class FormComputedOutputTypes
{
    public const string Text = "text";
    public const string Number = "number";

    public const int MaxLength = 10;

    public static readonly IReadOnlyList<string> All = [Text, Number];

    public static bool IsDefined(string? outputType) =>
        outputType is not null && All.Contains(outputType, StringComparer.Ordinal);
}
