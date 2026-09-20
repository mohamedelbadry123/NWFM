using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Commands.CloneForm;

/// <summary>Copies a form's working schema into a new Draft form under a new code. History is not copied.</summary>
[Authorize(Policy = NwfmPolicies.ManageForms)]
public sealed record CloneFormCommand : IRequest<Result<FormDetailDto>>
{
    public Guid Id { get; init; }
    public string NewCode { get; init; } = default!;
    public string NewNameEn { get; init; } = default!;
    public string NewNameAr { get; init; } = default!;
}
