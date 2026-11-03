using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Users;

public sealed record PlatformUserDto
{
    public Guid Id { get; init; }

    public string Email { get; init; } = default!;

    public string DisplayName { get; init; } = default!;

    public string Role { get; init; } = default!;

    public bool IsActive { get; init; }

    public DateTime? LastLoginAtUtc { get; init; }

    public DateTime? LockoutEndUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record GetPlatformUsersQuery : IRequest<Result<IReadOnlyList<PlatformUserDto>>>;

public sealed class GetPlatformUsersQueryHandler(ICatalogDbContext catalog)
    : IRequestHandler<GetPlatformUsersQuery, Result<IReadOnlyList<PlatformUserDto>>>
{
    public async Task<Result<IReadOnlyList<PlatformUserDto>>> Handle(GetPlatformUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await catalog.PlatformUsers
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new PlatformUserDto
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                Role = u.Role,
                IsActive = u.IsActive,
                LastLoginAtUtc = u.LastLoginAtUtc,
                LockoutEndUtc = u.LockoutEndUtc,
                CreatedAtUtc = u.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PlatformUserDto>>.Success(users);
    }
}

/// <summary>
/// Creates a platform user (no <see cref="Id"/>, password required) or updates one
/// (an empty password keeps the current one). E-mail is fixed once created.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record SavePlatformUserCommand : IRequest<Result<Guid>>
{
    public const int PasswordMinLength = 12;
    public const int PasswordMaxLength = 128;

    public Guid? Id { get; init; }

    public string Email { get; init; } = default!;

    public string DisplayName { get; init; } = default!;

    public string Role { get; init; } = PlatformRoles.Support;

    public bool IsActive { get; init; } = true;

    public string? Password { get; init; }
}

public sealed class SavePlatformUserCommandValidator : AbstractValidator<SavePlatformUserCommand>
{
    public SavePlatformUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-mail is required.")
            .EmailAddress().WithMessage("E-mail is not valid.")
            .MaximumLength(PlatformUser.EmailMaxLength).WithMessage($"E-mail must not exceed {PlatformUser.EmailMaxLength} characters.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(PlatformUser.NameMaxLength).WithMessage($"Display name must not exceed {PlatformUser.NameMaxLength} characters.");

        RuleFor(x => x.Role)
            .Must(PlatformRoles.IsDefined).WithMessage("Role is not recognized.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required for a new user.")
            .When(x => x.Id is null);

        RuleFor(x => x.Password)
            .MinimumLength(SavePlatformUserCommand.PasswordMinLength)
            .WithMessage($"Password must be at least {SavePlatformUserCommand.PasswordMinLength} characters.")
            .MaximumLength(SavePlatformUserCommand.PasswordMaxLength)
            .WithMessage($"Password must not exceed {SavePlatformUserCommand.PasswordMaxLength} characters.")
            .When(x => !string.IsNullOrEmpty(x.Password));
    }
}

public sealed class SavePlatformUserCommandHandler(
    ICatalogDbContext catalog,
    IPlatformCredentials credentials,
    IPlatformUser currentUser,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<SavePlatformUserCommand, Result<Guid>>
{
    private const string PendingPasswordHash = "pending";

    public async Task<Result<Guid>> Handle(SavePlatformUserCommand request, CancellationToken cancellationToken)
    {
        PlatformUser user;

        if (request.Id is { } id)
        {
            var existing = await catalog.PlatformUsers.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (existing is null)
            {
                return PlatformResults.Fail<Guid>("Platform user not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
            }

            // An admin cannot lock themselves out; another admin has to do it.
            if (existing.Id == currentUser.Id && (!request.IsActive || request.Role != PlatformRoles.Admin))
            {
                return PlatformResults.Fail<Guid>("You cannot deactivate or demote your own account.", ApiErrorCodes.BackendBusinessRuleValidation, httpStatusCode: 422);
            }

            user = existing;
            user.Update(request.DisplayName, request.Role, request.IsActive);
        }
        else
        {
            var normalized = PlatformUser.NormalizeEmail(request.Email);
            if (await catalog.PlatformUsers.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
            {
                return PlatformResults.Fail<Guid>("A platform user with this e-mail already exists.", ApiErrorCodes.ValidationError, httpStatusCode: 409);
            }

            user = PlatformUser.Create(request.Email, request.DisplayName, request.Role, PendingPasswordHash, timeProvider.GetUtcNow().UtcDateTime);
            if (!request.IsActive)
            {
                user.Update(request.DisplayName, request.Role, isActive: false);
            }

            catalog.PlatformUsers.Add(user);
        }

        if (!string.IsNullOrEmpty(request.Password))
        {
            user.ChangePasswordHash(credentials.HashPassword(user, request.Password));
        }

        audit.Add(PlatformAuditActions.PlatformUserSaved, null, nameof(PlatformUser), user.Id.ToString(),
            $"{user.Email} ({user.Role}, {(user.IsActive ? "active" : "inactive")})");
        await catalog.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(user.Id);
    }
}
