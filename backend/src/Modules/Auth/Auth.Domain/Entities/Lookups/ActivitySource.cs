using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities.Lookups;

/// <summary>Where an activity can come from: a system of ours, or one outside reached at its URL.</summary>
public static class ActivitySourceKinds
{
    public const string Internal = "Internal";
    public const string External = "External";

    public const int MaxLength = 20;

    public static readonly IReadOnlyList<string> All = [Internal, External];
}

/// <summary>A system activities are created from. An activity type names the sources allowed to create it.</summary>
public sealed class ActivitySource : Entity
{
    public const int UrlMaxLength = 500;

    private ActivitySource() { }
    public string Code { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public string Kind { get; private set; } = ActivitySourceKinds.Internal;
    public string? Url { get; private set; }
    public bool IsActive { get; private set; }

    public static ActivitySource Create(string code, string nameEn, string nameAr, string kind, string? url)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Source code is required.");
        var item = new ActivitySource { Code = code.Trim(), CreatedAt = DateTime.UtcNow, IsActive = true };
        item.Update(nameEn, nameAr, kind, url);
        return item;
    }

    public void Update(string nameEn, string nameAr, string kind, string? url)
    {
        if (string.IsNullOrWhiteSpace(nameEn))
            throw new DomainException("Name is required.");
        var matched = ActivitySourceKinds.All.FirstOrDefault(k => string.Equals(k, kind?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException("A source is either Internal or External.");
        url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        if (matched == ActivitySourceKinds.External && url is null)
            throw new DomainException("An external source needs its URL.");
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            throw new DomainException("The URL must be an absolute http or https address.");
        NameEn = nameEn.Trim(); NameAr = nameAr.Trim(); Kind = matched; Url = url;
        SetUpdated(DateTime.UtcNow);
    }

    public void SetActive(bool active) { IsActive = active; SetUpdated(DateTime.UtcNow); }
}
