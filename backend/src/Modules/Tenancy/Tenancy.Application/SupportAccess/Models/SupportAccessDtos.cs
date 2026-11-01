namespace Tenancy.Application.SupportAccess.Models;

public sealed record SupportAccessGrantDto
{
    public Guid Id { get; init; }

    public string Scope { get; init; } = default!;

    public string Reason { get; init; } = default!;

    public string GrantedByUserName { get; init; } = default!;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public DateTime? RevokedAtUtc { get; init; }

    public bool IsActive { get; init; }
}

public sealed record SupportAccessSessionDto
{
    public Guid Id { get; init; }

    public Guid GrantId { get; init; }

    public string PlatformUserName { get; init; } = default!;

    public string Scope { get; init; } = default!;

    public string TicketReference { get; init; } = default!;

    public string ImpersonatedUserName { get; init; } = default!;

    public DateTime StartedAtUtc { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public DateTime? EndedAtUtc { get; init; }

    public bool IsActive { get; init; }
}

public sealed record SupportAccessHistoryDto
{
    public IReadOnlyList<SupportAccessGrantDto> Grants { get; init; } = [];

    public IReadOnlyList<SupportAccessSessionDto> Sessions { get; init; } = [];

    public int MaxGrantHours { get; init; }
}

public sealed record SupportSessionEndedDto
{
    public Guid SessionId { get; init; }

    /// <summary>The platform console page to return the support user to.</summary>
    public string ReturnUrl { get; init; } = default!;
}
