using NWFM.Shared.Security;
using Tenancy.Application.SupportAccess.Models;

namespace Tenancy.Application.SupportAccess.Commands.EndCurrentSupportSession;

/// <summary>Ends the caller's own support session from inside the tenant app and says where to go back to.</summary>
[Authorize]
[AllowSupportReadOnly]
public sealed record EndCurrentSupportSessionCommand : IRequest<Result<SupportSessionEndedDto>>;
