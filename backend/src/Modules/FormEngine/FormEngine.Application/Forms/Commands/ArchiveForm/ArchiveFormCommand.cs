using FormEngine.Application.Forms.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Forms.Commands.ArchiveForm;

/// <summary>Archives a form in any other state. Archived forms are read-only and hidden from the working grid.</summary>
[Authorize(Policy = NwfmPolicies.ManageForms)]
public sealed record ArchiveFormCommand(Guid Id) : IRequest<Result<FormDetailDto>>;
