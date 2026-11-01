using NWFM.Shared.Constants;
using NWFM.Shared.Security;
using Tenancy.Application.SupportAccess.Models;

namespace Tenancy.Application.SupportAccess.Queries.GetSupportAccess;

/// <summary>The workspace's support-access grants and the sessions platform staff opened under them.</summary>
[Authorize(Policy = NwfmPolicies.ManageSupportAccess)]
public sealed record GetSupportAccessQuery : IRequest<Result<SupportAccessHistoryDto>>;
