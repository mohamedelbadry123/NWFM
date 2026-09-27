using FormEngine.Domain.Constants;

namespace FormEngine.Application.Submissions.Common;

/// <summary>Shapes a submission row as the API returns it.</summary>
public static class SubmissionRows
{
    /// <summary>
    /// Adds the <c>FormDefinitionId</c> key back onto a row. The column went away when each form got a
    /// table of its own — the table is the form — but a row read on its own should still say which
    /// form it answers, and clients already read it.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> WithForm(IReadOnlyDictionary<string, object?> row, Guid formDefinitionId)
    {
        var shaped = new Dictionary<string, object?>(row.Count + 1, StringComparer.Ordinal)
        {
            [FormSubmissionColumns.FormDefinitionId] = formDefinitionId,
        };

        foreach (var (key, value) in row)
        {
            shaped[key] = value;
        }

        return shaped;
    }

    /// <summary>The answers alone: every key that is not submission metadata.</summary>
    public static IReadOnlyDictionary<string, object?> AnswersOf(IReadOnlyDictionary<string, object?> row) =>
        row.Where(kvp => !FormSubmissionColumns.IsBase(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
}
