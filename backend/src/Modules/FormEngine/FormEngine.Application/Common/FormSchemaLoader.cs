using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FormEngine.Application.Common;

/// <summary>The schema a request is judged against, and the version it belongs to.</summary>
/// <param name="VersionNo">Null only when the form has never been published.</param>
internal sealed record ResolvedFormSchema(FormDefinition Form, int? VersionNo, FormSchema Schema);

/// <summary>
/// Resolves which schema a fill or an upload is checked against: the published version it names,
/// otherwise the form's current version. The working draft is used only for a form that has never
/// been published, so a redesign in progress can never change the rules under a fill already pinned
/// to a version.
/// </summary>
internal static class FormSchemaLoader
{
    public static async Task<ResolvedFormSchema?> LoadAsync(
        IFormEngineDbContext context,
        Guid formDefinitionId,
        int? requestedVersionNo,
        CancellationToken cancellationToken)
    {
        var form = await context.FormDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == formDefinitionId, cancellationToken);

        if (form is null)
        {
            return null;
        }

        var versionNo = requestedVersionNo ?? form.CurrentVersionNo;

        if (versionNo is null)
        {
            return new ResolvedFormSchema(form, null, FormSchemaParser.Parse(form.SchemaJson));
        }

        var schemaJson = await context.FormVersions
            .AsNoTracking()
            .Where(x => x.FormDefinitionId == form.Id
                && x.VersionNo == versionNo
                && x.TargetClient == FormTargetClients.Formly)
            .Select(x => x.SchemaJson)
            .FirstOrDefaultAsync(cancellationToken);

        return schemaJson is null
            ? null
            : new ResolvedFormSchema(form, versionNo, FormSchemaParser.Parse(schemaJson));
    }
}
