using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Commands.CreateForm;

/// <summary>Creates a form's main info. The fields are designed afterwards in the form builder.</summary>
[Authorize(Policy = NwfmPolicies.ManageForms)]
public sealed record CreateFormCommand : IRequest<Result<FormDetailDto>>
{
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string Category { get; init; } = default!;
    public string? DepartmentCode { get; init; }
}
