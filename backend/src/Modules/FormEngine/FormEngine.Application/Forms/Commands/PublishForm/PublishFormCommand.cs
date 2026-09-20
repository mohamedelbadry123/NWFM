using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Commands.PublishForm;

/// <summary>
/// Freezes the working schema into a new version: registers its data names in the field catalog and
/// adds their columns to <c>FE.Submissions</c>. A data name already registered under another type is
/// refused with a conflict, and nothing is changed.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageForms)]
public sealed record PublishFormCommand(Guid Id) : IRequest<Result<FormDetailDto>>;
