namespace FormEngine.Domain.Constants;

/// <summary>
/// Comparisons a visibility or requirement condition can make. Mirrors <c>RULE_OPERATORS</c> in the
/// Angular builder (<c>form-schema.types.ts</c>) — the same string travels in the form schema JSON
/// and is evaluated on both sides, so the two must stay in sync.
/// </summary>
/// <remarks>
/// An operator this class does not know is treated as satisfied, matching the builder's own
/// <c>default: () =&gt; true</c>. A schema written by a newer client therefore relaxes the rule
/// rather than hiding a field nobody can reveal.
/// </remarks>
public static class FormRuleOperators
{
    public const string Equal = "equal";
    public const string NotEqual = "not_equal";
    public const string Contains = "contains";
    public const string StartsWith = "starts_with";
    public const string GreaterThan = "greater_than";
    public const string LessThan = "less_than";
    public const string IsEmpty = "is_empty";
    public const string IsNotEmpty = "is_not_empty";
}

/// <summary>
/// How a rule group combines its conditions. Mirrors <c>RULE_MATCH</c> in the Angular builder.
/// </summary>
public static class FormRuleMatches
{
    public const string All = "all";
    public const string Any = "any";
}
