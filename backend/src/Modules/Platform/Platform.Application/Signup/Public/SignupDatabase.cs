using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Application.Signup.Common;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Signup.Public;

public sealed record ByoSetupScriptDto(string Script, IReadOnlyList<string> EgressIps);

/// <summary>The T-SQL a customer runs on their own server before entering its details.</summary>
public sealed record GetByoSetupScriptQuery(string Database, string UserName, string RuntimeUserName) : IRequest<Result<ByoSetupScriptDto>>;

public sealed class GetByoSetupScriptQueryValidator : AbstractValidator<GetByoSetupScriptQuery>
{
    public GetByoSetupScriptQueryValidator()
    {
        RuleFor(x => x.Database)
            .NotEmpty().WithMessage("Database name is required.")
            .Matches("^[A-Za-z0-9_-]{1,128}$").WithMessage("The database name may contain letters, digits, '_' and '-' only.");

        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("User name is required.")
            .Matches("^[A-Za-z0-9_-]{1,128}$").WithMessage("The user name may contain letters, digits, '_' and '-' only.");

        RuleFor(x => x.RuntimeUserName)
            .NotEmpty().WithMessage("Runtime user name is required.")
            .Matches("^[A-Za-z0-9_-]{1,128}$").WithMessage("The runtime user name may contain letters, digits, '_' and '-' only.")
            .NotEqual(x => x.UserName, StringComparer.OrdinalIgnoreCase).WithMessage("The runtime user must differ from the owner.");
    }
}

public sealed class GetByoSetupScriptQueryHandler(IByoDatabaseInspector inspector, IOptions<PlatformOptions> platformOptions)
    : IRequestHandler<GetByoSetupScriptQuery, Result<ByoSetupScriptDto>>
{
    public Task<Result<ByoSetupScriptDto>> Handle(GetByoSetupScriptQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<ByoSetupScriptDto>.Success(
            new ByoSetupScriptDto(inspector.BuildSetupScript(request.Database, request.UserName, request.RuntimeUserName), platformOptions.Value.EgressIps)));
}

/// <summary>
/// Connection details for a bring-your-own signup. Only usable with the signup's confirmation
/// token, i.e. by whoever received the e-mail: the connection test is not open to the internet.
/// </summary>
public abstract record SignupDatabaseRequest
{
    public Guid SignupId { get; init; }

    public string Token { get; init; } = default!;

    public string Server { get; init; } = default!;

    public int? Port { get; init; }

    public string Database { get; init; } = default!;

    public string UserName { get; init; } = default!;

    public string Password { get; init; } = default!;

    /// <summary>The restricted principal requests run as. It must hold no database role.</summary>
    public string RuntimeUserName { get; init; } = default!;

    public string RuntimePassword { get; init; } = default!;

    public bool TrustServerCertificate { get; init; }

    public ByoDatabaseDetails ToDetails() =>
        new(Server.Trim(), Port, Database.Trim(), UserName.Trim(), Password, RuntimeUserName.Trim(), RuntimePassword, TrustServerCertificate);
}

public abstract class SignupDatabaseRequestValidator<T> : AbstractValidator<T> where T : SignupDatabaseRequest
{
    protected SignupDatabaseRequestValidator()
    {
        RuleFor(x => x.SignupId).NotEmpty().WithMessage("Signup id is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Confirmation token is required.");
        RuleFor(x => x.Server)
            .NotEmpty().WithMessage("Server is required.")
            .MaximumLength(SignupRequest.ServerMaxLength).WithMessage($"Server must not exceed {SignupRequest.ServerMaxLength} characters.");
        RuleFor(x => x.Port).InclusiveBetween(1, 65535).WithMessage("Port must be between 1 and 65535.");
        RuleFor(x => x.Database)
            .NotEmpty().WithMessage("Database name is required.")
            .MaximumLength(SignupRequest.DatabaseNameMaxLength).WithMessage($"Database name must not exceed {SignupRequest.DatabaseNameMaxLength} characters.");
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("User name is required.")
            .MaximumLength(SignupRequest.DatabaseNameMaxLength).WithMessage($"User name must not exceed {SignupRequest.DatabaseNameMaxLength} characters.");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.");
        RuleFor(x => x.RuntimeUserName)
            .NotEmpty().WithMessage("Runtime user name is required.")
            .MaximumLength(SignupRequest.DatabaseNameMaxLength).WithMessage($"Runtime user name must not exceed {SignupRequest.DatabaseNameMaxLength} characters.")
            .Must((request, runtime) => !string.Equals(runtime?.Trim(), request.UserName?.Trim(), StringComparison.OrdinalIgnoreCase))
            .WithMessage("The runtime user must differ from the owner.");
        RuleFor(x => x.RuntimePassword)
            .NotEmpty().WithMessage("Runtime password is required.")
            .MaximumLength(128).WithMessage("Runtime password must not exceed 128 characters.");
    }
}

internal static class SignupDatabaseAccess
{
    public const string NotAllowed = "This signup does not accept database details now.";

    /// <summary>The bring-your-own signup the token unlocks, or null.</summary>
    public static async Task<SignupRequest?> FindAsync(ICatalogDbContext catalog, SignupDatabaseRequest request, DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var signup = await catalog.SignupRequests.FirstOrDefaultAsync(s => s.Id == request.SignupId, cancellationToken);

        return signup is not null
            && signup.DatabaseMode == TenantDatabaseMode.BringYourOwn
            && signup.IsEmailVerified
            && signup.VerifyEmail(SignupTokens.Hash(request.Token), utcNow)
            ? signup
            : null;
    }
}

public sealed record TestSignupDatabaseCommand : SignupDatabaseRequest, IRequest<Result<ByoDatabaseInspection>>;

public sealed class TestSignupDatabaseCommandValidator : SignupDatabaseRequestValidator<TestSignupDatabaseCommand>;

public sealed class TestSignupDatabaseCommandHandler(ICatalogDbContext catalog, IByoDatabaseInspector inspector, TimeProvider timeProvider)
    : IRequestHandler<TestSignupDatabaseCommand, Result<ByoDatabaseInspection>>
{
    public async Task<Result<ByoDatabaseInspection>> Handle(TestSignupDatabaseCommand request, CancellationToken cancellationToken)
    {
        if (await SignupDatabaseAccess.FindAsync(catalog, request, timeProvider.GetUtcNow().UtcDateTime, cancellationToken) is null)
        {
            return PlatformResults.Fail<ByoDatabaseInspection>(SignupDatabaseAccess.NotAllowed, ApiErrorCodes.SignupInvalidState, httpStatusCode: 409);
        }

        return Result<ByoDatabaseInspection>.Success(await inspector.InspectAsync(request.ToDetails(), cancellationToken));
    }
}

public sealed record SaveSignupDatabaseDto(ByoDatabaseInspection Inspection, SignupStatusDto Status);

/// <summary>
/// Tests the database and, when every check passes, stores the details (password encrypted) and
/// queues provisioning. A failed check changes nothing; the result lists what to fix.
/// </summary>
public sealed record SaveSignupDatabaseCommand : SignupDatabaseRequest, IRequest<Result<SaveSignupDatabaseDto>>;

public sealed class SaveSignupDatabaseCommandValidator : SignupDatabaseRequestValidator<SaveSignupDatabaseCommand>;

public sealed class SaveSignupDatabaseCommandHandler(
    ICatalogDbContext catalog,
    IByoDatabaseInspector inspector,
    ISecretProtector secretProtector,
    ISignupProvisioningQueue provisioningQueue,
    IOptions<PlatformOptions> platformOptions,
    TimeProvider timeProvider)
    : IRequestHandler<SaveSignupDatabaseCommand, Result<SaveSignupDatabaseDto>>
{
    public async Task<Result<SaveSignupDatabaseDto>> Handle(SaveSignupDatabaseCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var signup = await SignupDatabaseAccess.FindAsync(catalog, request, now, cancellationToken);

        // Once the database has been validated, later steps run against it; swapping it then would
        // leave a half-provisioned tenant split across two databases.
        if (signup is null
            || signup.Status is not (SignupStatus.AwaitingDatabase or SignupStatus.Failed)
            || signup.IsStepDone(ProvisioningStep.ValidateDatabase))
        {
            return PlatformResults.Fail<SaveSignupDatabaseDto>(SignupDatabaseAccess.NotAllowed, ApiErrorCodes.SignupInvalidState, httpStatusCode: 409);
        }

        var details = request.ToDetails();
        var inspection = await inspector.InspectAsync(details, cancellationToken);

        if (!inspection.Succeeded)
        {
            return Result<SaveSignupDatabaseDto>.Success(new SaveSignupDatabaseDto(inspection, signup.ToStatusDto(platformOptions.Value)));
        }

        signup.SetBringYourOwnDatabase(details.Server, details.Port, details.Database, details.UserName,
            secretProtector.Protect(SecretPurposes.SignupDatabasePassword, details.Password),
            details.RuntimeUserName, secretProtector.Protect(SecretPurposes.SignupDatabasePassword, details.RuntimePassword),
            details.TrustServerCertificate, now);
        signup.Queue(now);
        await catalog.SaveChangesAsync(cancellationToken);

        provisioningQueue.Enqueue(signup.Id);

        return Result<SaveSignupDatabaseDto>.Success(new SaveSignupDatabaseDto(inspection, signup.ToStatusDto(platformOptions.Value)));
    }
}
