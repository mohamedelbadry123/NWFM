using Platform.Application.Common.Security;
using Platform.Application.Tenants.Common;
using Platform.Application.Tenants.Models;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Tenants.Commands;

[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record UpdatePlatformTenantCommand : IRequest<Result<PlatformTenantDetailDto>>
{
    public Guid TenantId { get; init; }

    public string Name { get; init; } = default!;

    public string ContactName { get; init; } = default!;

    public string ContactEmail { get; init; } = default!;

    public string? ContactPhone { get; init; }

    public bool SsoEnabled { get; init; }

    public string? SsoEntityId { get; init; }

    public string? SsoIdentityProviderEntityId { get; init; }

    public string? SsoMetadataUrl { get; init; }

    public string DefaultLanguage { get; init; } = TenantSettings.Languages.English;

    /// <summary>Show the workspace on the landing page's sign-in list. Off unless the customer asks for it.</summary>
    public bool ListedPublicly { get; init; }

    /// <summary>Web hosts that resolve to this tenant besides <c>{key}.{RootDomain}</c>. Replaces the set.</summary>
    public IReadOnlyList<string> Hosts { get; init; } = [];
}

public sealed class UpdatePlatformTenantCommandValidator : AbstractValidator<UpdatePlatformTenantCommand>
{
    public UpdatePlatformTenantCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Workspace name is required.")
            .MaximumLength(Tenant.NameMaxLength).WithMessage($"Workspace name must not exceed {Tenant.NameMaxLength} characters.");

        RuleFor(x => x.ContactName)
            .MaximumLength(Tenant.ContactMaxLength).WithMessage($"Contact name must not exceed {Tenant.ContactMaxLength} characters.");

        RuleFor(x => x.ContactEmail)
            .NotEmpty().WithMessage("Contact e-mail is required.")
            .EmailAddress().WithMessage("Contact e-mail is not a valid address.");

        RuleFor(x => x.DefaultLanguage)
            .Must(lang => lang is TenantSettings.Languages.English or TenantSettings.Languages.Arabic)
            .WithMessage("Default language must be 'en' or 'ar'.");

        RuleForEach(x => x.Hosts)
            .NotEmpty().WithMessage("A host cannot be empty.")
            .MaximumLength(TenantHost.HostMaxLength).WithMessage($"A host must not exceed {TenantHost.HostMaxLength} characters.")
            .Must(host => Uri.CheckHostName(TenantHost.Normalize(host)) is UriHostNameType.Dns)
            .WithMessage("A host must be a DNS name without scheme, port or path.");
    }
}

public sealed class UpdatePlatformTenantCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    IPlatformAuditWriter audit)
    : IRequestHandler<UpdatePlatformTenantCommand, Result<PlatformTenantDetailDto>>
{
    public async Task<Result<PlatformTenantDetailDto>> Handle(UpdatePlatformTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await PlatformTenantQueries.LoadAsync(catalog, request.TenantId, cancellationToken);

        if (tenant is null)
        {
            return PlatformResults.Fail<PlatformTenantDetailDto>("Workspace not found.", ApiErrorCodes.TenantNotFound, httpStatusCode: 404);
        }

        var hosts = request.Hosts.Select(TenantHost.Normalize).Where(h => h.Length > 0).Distinct(StringComparer.Ordinal).ToList();

        // A host belongs to one tenant only; the unique index would also refuse it, with a 500.
        var taken = await catalog.TenantHosts
            .AsNoTracking()
            .Where(h => hosts.Contains(h.Host) && h.TenantId != tenant.Id)
            .Select(h => h.Host)
            .ToListAsync(cancellationToken);

        if (taken.Count > 0)
        {
            return PlatformResults.Fail<PlatformTenantDetailDto>(
                $"Host(s) already used by another workspace: {string.Join(", ", taken)}.",
                ApiErrorCodes.ValidationError,
                httpStatusCode: 409);
        }

        tenant.UpdateProfile(request.Name, request.ContactName, request.ContactEmail, request.ContactPhone);
        tenant.ChangeSettings(TenantSettings.Create(
            request.SsoEnabled,
            request.DefaultLanguage,
            request.SsoEntityId,
            request.SsoIdentityProviderEntityId,
            request.SsoMetadataUrl,
            request.ListedPublicly));

        foreach (var existing in tenant.Hosts.Select(h => h.Host).Except(hosts, StringComparer.Ordinal).ToList())
        {
            tenant.RemoveHost(existing);
        }

        foreach (var host in hosts)
        {
            tenant.AddHost(host);
        }

        audit.Add(PlatformAuditActions.TenantUpdated, tenant.Id, nameof(Tenant), tenant.Key);
        await catalog.SaveChangesAsync(cancellationToken);

        PlatformTenantQueries.Publish(tenantStore);
        return Result<PlatformTenantDetailDto>.Success(tenant.ToDetailDto());
    }
}
