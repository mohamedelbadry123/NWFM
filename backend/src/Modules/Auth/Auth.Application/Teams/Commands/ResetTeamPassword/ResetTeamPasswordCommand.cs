using Auth.Application.Common.Interfaces;
using Auth.Application.Teams.Common;
using FluentValidation;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Teams.Commands.ResetTeamPassword;

/// <summary>Sets a new password on a team's login. Team logins are hidden from user administration, so this is the only way.</summary>
[Authorize(Policy = NwfmPolicies.ManageTeams)]
public sealed record ResetTeamPasswordCommand : IRequest<Result<string>>
{
    public Guid TeamId { get; init; }
    public string NewPassword { get; init; } = default!;
}

public sealed class ResetTeamPasswordCommandValidator : AbstractValidator<ResetTeamPasswordCommand>
{
    public ResetTeamPasswordCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("A new password is required.")
            .MinimumLength(TeamRules.PasswordMinLength).WithMessage($"Password must be at least {TeamRules.PasswordMinLength} characters.");
    }
}

public sealed class ResetTeamPasswordCommandHandler(IUserAccountService accounts)
    : IRequestHandler<ResetTeamPasswordCommand, Result<string>>
{
    public Task<Result<string>> Handle(ResetTeamPasswordCommand request, CancellationToken ct) =>
        accounts.ResetTeamLoginPasswordAsync(request.TeamId, request.NewPassword, ct);
}
