using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Commands.UpdateForm;

/// <summary>
/// Updates a form's main info. Not a schema change, so a Published form stays Published; the code is
/// immutable because published versions and pinned consumers are known by it.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageForms)]
public sealed record UpdateFormCommand : IRequest<Result<FormDetailDto>>
{
    public Guid Id { get; init; }
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string Category { get; init; } = default!;
    public string? DepartmentCode { get; init; }
}
