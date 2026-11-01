using NWFM.Shared.MultiTenancy;
using Tenancy.Domain.Entities;

namespace Platform.Api.Contracts;

// Query-string shapes for the platform list endpoints. Positional on purpose: [AsParameters]
// ignores property initializers, so defaults have to live on the constructor.

public sealed record TenantListQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    TenantStatus? Status = null,
    string? SortField = null,
    bool SortDescending = false);

public sealed record SignupListQuery(
    int PageNumber = 1,
    int PageSize = 20,
    SignupStatus? Status = null,
    string? SearchTerm = null);

public sealed record AuditListQuery(
    int PageNumber = 1,
    int PageSize = 50,
    Guid? TenantId = null,
    string? Action = null,
    string? SearchTerm = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);
