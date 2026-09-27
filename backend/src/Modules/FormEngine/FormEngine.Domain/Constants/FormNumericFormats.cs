namespace FormEngine.Domain.Constants;

/// <summary>
/// How a <c>numeric</c> field's answer is written. Mirrors <c>NUMERIC_FORMATS</c> in the Angular
/// builder (<c>form-schema.types.ts</c>).
/// </summary>
public static class FormNumericFormats
{
    public const string Decimal = "decimal";

    /// <summary>Whole numbers only — the fill form adds an <c>integer</c> validator for it.</summary>
    public const string Integer = "integer";
}
