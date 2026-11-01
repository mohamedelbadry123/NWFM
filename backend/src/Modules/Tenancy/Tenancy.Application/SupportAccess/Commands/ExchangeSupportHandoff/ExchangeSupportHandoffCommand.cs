namespace Tenancy.Application.SupportAccess.Commands.ExchangeSupportHandoff;

/// <summary>
/// The tenant app's half of a support handoff: spends the one-time code the platform console put in
/// the URL fragment and returns a tenant token for the session. Anonymous — the code is the proof —
/// and bound to the workspace the request resolved to, so a code for one tenant is useless on another.
/// </summary>
public sealed record ExchangeSupportHandoffCommand : IRequest<Result<SupportSessionTokenDto>>
{
    public string Code { get; init; } = default!;
}
