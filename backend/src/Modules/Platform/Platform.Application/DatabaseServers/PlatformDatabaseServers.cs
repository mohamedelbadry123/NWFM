using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.DatabaseServers;

/// <summary>A SQL Server we create managed tenant databases on. The admin connection string is never returned.</summary>
public sealed record PlatformDatabaseServerDto
{
    public int Id { get; init; }

    public string Name { get; init; } = default!;

    public bool UseContainedUsers { get; init; }

    public int MaxDatabases { get; init; }

    public int DatabaseCount { get; init; }

    public bool IsActive { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record GetPlatformDatabaseServersQuery : IRequest<Result<IReadOnlyList<PlatformDatabaseServerDto>>>;

public sealed class GetPlatformDatabaseServersQueryHandler(ICatalogDbContext catalog)
    : IRequestHandler<GetPlatformDatabaseServersQuery, Result<IReadOnlyList<PlatformDatabaseServerDto>>>
{
    public async Task<Result<IReadOnlyList<PlatformDatabaseServerDto>>> Handle(
        GetPlatformDatabaseServersQuery request,
        CancellationToken cancellationToken)
    {
        var servers = await catalog.DatabaseServers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new PlatformDatabaseServerDto
            {
                Id = s.Id,
                Name = s.Name,
                UseContainedUsers = s.UseContainedUsers,
                MaxDatabases = s.MaxDatabases,
                DatabaseCount = catalog.TenantDatabases.Count(d => d.DatabaseServerId == s.Id),
                IsActive = s.IsActive,
                CreatedAtUtc = s.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PlatformDatabaseServerDto>>.Success(servers);
    }
}

/// <summary>
/// Registers a server (no <see cref="Id"/>) or updates one. On update, an empty
/// <see cref="AdminConnectionString"/> keeps the stored one.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record SavePlatformDatabaseServerCommand : IRequest<Result<int>>
{
    public const int ConnectionStringMaxLength = 2000;

    public int? Id { get; init; }

    public string Name { get; init; } = default!;

    public string? AdminConnectionString { get; init; }

    public bool UseContainedUsers { get; init; }

    public int MaxDatabases { get; init; } = 100;

    public bool IsActive { get; init; } = true;
}

public sealed class SavePlatformDatabaseServerCommandValidator : AbstractValidator<SavePlatformDatabaseServerCommand>
{
    public SavePlatformDatabaseServerCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Server name is required.")
            .MaximumLength(DatabaseServer.NameMaxLength).WithMessage($"Server name must not exceed {DatabaseServer.NameMaxLength} characters.");

        RuleFor(x => x.AdminConnectionString)
            .NotEmpty().WithMessage("Admin connection string is required for a new server.")
            .When(x => x.Id is null);

        RuleFor(x => x.AdminConnectionString)
            .MaximumLength(SavePlatformDatabaseServerCommand.ConnectionStringMaxLength)
            .WithMessage($"Connection string must not exceed {SavePlatformDatabaseServerCommand.ConnectionStringMaxLength} characters.");

        RuleFor(x => x.MaxDatabases).InclusiveBetween(1, 10000).WithMessage("Database limit must be between 1 and 10000.");
    }
}

public sealed class SavePlatformDatabaseServerCommandHandler(
    ICatalogDbContext catalog,
    ISecretProtector secretProtector,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<SavePlatformDatabaseServerCommand, Result<int>>
{
    public async Task<Result<int>> Handle(SavePlatformDatabaseServerCommand request, CancellationToken cancellationToken)
    {
        var encrypted = string.IsNullOrWhiteSpace(request.AdminConnectionString)
            ? null
            : secretProtector.Protect(SecretPurposes.DatabaseServerAdmin, request.AdminConnectionString.Trim());

        DatabaseServer server;

        if (request.Id is { } id)
        {
            var existing = await catalog.DatabaseServers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (existing is null)
            {
                return PlatformResults.Fail<int>("Database server not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
            }

            server = existing;
            server.Update(request.Name, request.UseContainedUsers, request.MaxDatabases, request.IsActive);
            if (encrypted is not null)
            {
                server.ReplaceAdminConnectionString(encrypted);
            }
        }
        else
        {
            server = DatabaseServer.Create(request.Name, encrypted!, request.UseContainedUsers, request.MaxDatabases,
                timeProvider.GetUtcNow().UtcDateTime);
            if (!request.IsActive)
            {
                server.Update(request.Name, request.UseContainedUsers, request.MaxDatabases, isActive: false);
            }

            catalog.DatabaseServers.Add(server);
        }

        audit.Add(PlatformAuditActions.DatabaseServerSaved, null, nameof(DatabaseServer), request.Name,
            encrypted is null ? null : "Admin connection string replaced.");
        await catalog.SaveChangesAsync(cancellationToken);

        return Result<int>.Success(server.Id);
    }
}
