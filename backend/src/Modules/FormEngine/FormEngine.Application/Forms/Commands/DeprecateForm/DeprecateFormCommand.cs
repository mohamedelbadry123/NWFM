using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Commands.DeprecateForm;

/// <summary>Retires a Published form: its versions stay readable, but it takes no new submissions.</summary>
[Authorize(Policy = NwfmPolicies.ManageForms)]
public sealed record DeprecateFormCommand(Guid Id) : IRequest<Result<FormDetailDto>>;
