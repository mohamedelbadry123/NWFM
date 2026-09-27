namespace Auth.Domain.Constants;

public static class OrgScopeLevels
{
    public const string Cluster = nameof(Cluster);
    public const string Cbu = nameof(Cbu);
    public const string Branch = nameof(Branch);
    public const string OperationArea = nameof(OperationArea);

    private static readonly HashSet<string> Defined = new(StringComparer.OrdinalIgnoreCase)
    {
        Cluster, Cbu, Branch, OperationArea
    };

    public static bool IsDefined(string? level) =>
        level is not null && Defined.Contains(level);
}
