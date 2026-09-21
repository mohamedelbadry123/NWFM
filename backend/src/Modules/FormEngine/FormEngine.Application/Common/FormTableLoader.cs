using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using Microsoft.EntityFrameworkCore;

namespace FormEngine.Application.Common;

/// <summary>
/// Resolves a form's submission table from its registry rows. Reads never ask SQL Server which
/// columns exist: <c>FormEngine.FormFields</c> is written in the same transaction that creates them, so it is
/// the list of columns by construction.
/// </summary>
public static class FormTableLoader
{
    /// <summary>The form's table, or null when the form has never been published and so has none.</summary>
    public static async Task<FormTable?> LoadAsync(
        IFormEngineDbContext context,
        Guid formDefinitionId,
        CancellationToken cancellationToken)
    {
        var tableName = await context.FormDefinitions
            .AsNoTracking()
            .Where(x => x.Id == formDefinitionId)
            .Select(x => x.SubmissionTable)
            .FirstOrDefaultAsync(cancellationToken);

        if (tableName is null)
        {
            return null;
        }

        var fields = await context.FormFields
            .AsNoTracking()
            .Where(x => x.FormDefinitionId == formDefinitionId)
            .Select(x => new { x.DataName, x.FieldType })
            .ToListAsync(cancellationToken);

        return FormTable.Create(
            formDefinitionId,
            tableName,
            fields.Select(f => new KeyValuePair<string, string>(f.DataName, f.FieldType)));
    }
}
