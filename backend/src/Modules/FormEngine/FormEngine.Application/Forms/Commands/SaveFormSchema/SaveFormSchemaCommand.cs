using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Commands.SaveFormSchema;

/// <summary>
/// Saves the form builder's document as the form's working draft. Editing a Published form reopens
/// it as a Draft revision; its published versions — and whatever is pinned to them — are untouched.
/// No catalog entry or column is created until the draft is published.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageForms)]
public sealed record SaveFormSchemaCommand : IRequest<Result<FormDetailDto>>
{
    public Guid Id { get; init; }

    /// <summary>The form-builder document (name_en/name_ar/elements).</summary>
    public string SchemaJson { get; init; } = default!;
}
