using System.Text;
using System.Text.RegularExpressions;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// Names a form's submission table after its code, so a DBA reading the schema can tell which table
/// is which: <c>SRV-FIELD-SURVEY-002</c> becomes <c>SUB_SRV_FIELD_SURVEY_002</c>.
///
/// The name is a SQL identifier interpolated into DDL, so it is built from a closed alphabet —
/// uppercase letters, digits and underscore — rather than escaped. Two codes can collapse to the same
/// name (<c>A-B</c> and <c>A_B</c>); <see cref="Candidates"/> yields numbered alternatives for that.
/// </summary>
public static partial class FormSubmissionTableName
{
    /// <summary>Leaves room under the 128-character identifier limit for a collision suffix.</summary>
    private const int BaseMaxLength = FormDefinition.SubmissionTableMaxLength - 8;

    /// <summary>Enough for any realistic run of colliding codes; past it something else is wrong.</summary>
    private const int MaxCollisionSuffix = 999;

    /// <summary>The first name tried for a form code.</summary>
    public static string For(string formCode)
    {
        var builder = new StringBuilder(FormEngineSchema.SubmissionTablePrefix);

        foreach (var character in formCode.Trim().ToUpperInvariant())
        {
            builder.Append(IsAllowed(character) ? character : '_');
        }

        var name = builder.ToString();
        return name.Length <= BaseMaxLength ? name : name[..BaseMaxLength];
    }

    /// <summary>The name for a form code, then <c>_2</c>, <c>_3</c>… for when the earlier ones are taken.</summary>
    public static IEnumerable<string> Candidates(string formCode)
    {
        var name = For(formCode);
        yield return name;

        for (var suffix = 2; suffix <= MaxCollisionSuffix; suffix++)
        {
            yield return $"{name}_{suffix}";
        }
    }

    /// <summary>
    /// Whether <paramref name="tableName"/> is one this class could have produced. The store re-checks
    /// every name it is handed before building SQL with it, so a value edited in the database cannot
    /// become an injection.
    /// </summary>
    public static bool IsValid(string? tableName) =>
        tableName is not null
        && tableName.Length <= FormDefinition.SubmissionTableMaxLength
        && TableNameRegex().IsMatch(tableName);

    private static bool IsAllowed(char character) =>
        character is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

    [GeneratedRegex("^SUB_[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TableNameRegex();
}
