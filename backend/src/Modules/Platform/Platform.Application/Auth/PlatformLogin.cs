using Platform.Application.Common.Interfaces;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Auth;

public sealed record PlatformLoginCommand : IRequest<Result<PlatformLoginDto>>
{
    public string Email { get; init; } = default!;

    public string Password { get; init; } = default!;
}

public sealed record PlatformLoginDto
{
    public string AccessToken { get; init; } = default!;

    public int ExpiresInSeconds { get; init; }

    public Guid UserId { get; init; }

    public string Email { get; init; } = default!;

    public string DisplayName { get; init; } = default!;

    public string Role { get; init; } = default!;
}

public sealed class PlatformLoginCommandValidator : AbstractValidator<PlatformLoginCommand>
{
    public PlatformLoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(PlatformUser.EmailMaxLength).WithMessage("Email is too long.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public sealed class PlatformLoginCommandHandler(
    ICatalogDbContext catalog,
    IPlatformCredentials credentials,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<PlatformLoginCommand, Result<PlatformLoginDto>>
{
    /// <summary>One message for every failure, so the endpoint does not reveal which accounts exist.</summary>
    private const string InvalidCredentials = "Invalid email or password.";

    private const string NotConfigured = "Platform sign-in is not configured.";

    public async Task<Result<PlatformLoginDto>> Handle(PlatformLoginCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var normalizedEmail = PlatformUser.NormalizeEmail(request.Email);
        var user = await catalog.PlatformUsers.FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive || user.IsLockedOut(now))
        {
            audit.Add(PlatformAuditActions.PlatformLoginFailed, null, nameof(PlatformUser), normalizedEmail);
            await catalog.SaveChangesAsync(cancellationToken);
            return Fail(InvalidCredentials);
        }

        var check = credentials.VerifyPassword(user, request.Password);

        if (check == PlatformPasswordCheck.Failed)
        {
            user.RecordFailedLogin(now);
            audit.Add(PlatformAuditActions.PlatformLoginFailed, null, nameof(PlatformUser), user.Id.ToString());
            await catalog.SaveChangesAsync(cancellationToken);
            return Fail(InvalidCredentials);
        }

        var token = credentials.IssueToken(user);
        if (token is null)
        {
            return PlatformResults.Fail<PlatformLoginDto>(NotConfigured, ApiErrorCodes.InternalServiceError, httpStatusCode: 500);
        }

        if (check == PlatformPasswordCheck.SuccessRehashNeeded)
        {
            user.ChangePasswordHash(credentials.HashPassword(user, request.Password));
        }

        user.RecordSuccessfulLogin(now);
        audit.Add(PlatformAuditActions.PlatformLogin, null, nameof(PlatformUser), user.Id.ToString());
        await catalog.SaveChangesAsync(cancellationToken);

        return Result<PlatformLoginDto>.Success(new PlatformLoginDto
        {
            AccessToken = token.AccessToken,
            ExpiresInSeconds = token.ExpiresInSeconds,
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role
        });
    }

    private static Result<PlatformLoginDto> Fail(string message) =>
        PlatformResults.Fail<PlatformLoginDto>(message, ApiErrorCodes.AuthenticationFailed, httpStatusCode: 401);
}
