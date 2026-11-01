using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace NWFM.Shared.Http;

/// <summary>
/// The single answer to "who is calling". The IP allowlist, the rate-limit partition key, and the
/// audit trail must agree on this or they protect different things: a request could be counted
/// against one identity and allowed against another.
/// </summary>
/// <remarks>
/// <para>
/// <c>X-Forwarded-For</c> is read <b>only</b> when the caller explicitly says a trusted proxy sits
/// in front. A client can put anything in that header, so trusting it unconditionally hands every
/// caller the ability to choose their own identity — which is exactly what an allowlist must not
/// allow. The flag is off by default; turning it on is a statement that the app is unreachable
/// except through the proxy.
/// </para>
/// <para>
/// Only the <b>first</b> entry is used. The header is a chain — <c>client, proxy1, proxy2</c> —
/// and the leftmost value is the client as the load balancer saw it. Walking the chain would only
/// pick a hop we trust even less.
/// </para>
/// </remarks>
public static class ClientIpResolver
{
    /// <summary>Returned when neither the header nor the socket yields an address.</summary>
    public const string UnknownIp = "unknown";

    /// <summary>
    /// The caller's address as text, for logging and for use as a rate-limit partition key.
    /// Returns <see cref="UnknownIp"/> rather than null so a partition key always exists — callers
    /// we cannot identify share one bucket instead of escaping the limiter entirely.
    /// </summary>
    public static string Resolve(HttpContext context, bool trustForwardedHeader)
    {
        ArgumentNullException.ThrowIfNull(context);

        var forwarded = ReadForwardedFor(context, trustForwardedHeader);
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded;
        }

        var remote = context.Connection.RemoteIpAddress;
        return remote is null ? UnknownIp : Normalize(remote).ToString();
    }

    /// <summary>
    /// The caller's address parsed, for allowlist comparison. False means we could not establish
    /// who is calling — an allowlist has to treat that as a denial, never as a pass.
    /// </summary>
    public static bool TryResolveAddress(
        HttpContext context,
        bool trustForwardedHeader,
        [NotNullWhen(true)] out IPAddress? address)
    {
        ArgumentNullException.ThrowIfNull(context);

        var forwarded = ReadForwardedFor(context, trustForwardedHeader);
        if (!string.IsNullOrWhiteSpace(forwarded) && IPAddress.TryParse(forwarded, out var parsed))
        {
            address = Normalize(parsed);
            return true;
        }

        var remote = context.Connection.RemoteIpAddress;
        if (remote is not null)
        {
            address = Normalize(remote);
            return true;
        }

        address = null;
        return false;
    }

    /// <summary>
    /// Collapses <c>::ffff:10.0.0.1</c> to <c>10.0.0.1</c>. Kestrel reports IPv4 callers in this
    /// mapped form on a dual-stack socket, so without this an allowlist entry of <c>10.0.0.1</c>
    /// would never match the caller it was written for.
    /// </summary>
    public static IPAddress Normalize(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static string? ReadForwardedFor(HttpContext context, bool trustForwardedHeader)
    {
        if (!trustForwardedHeader)
        {
            return null;
        }

        if (!context.Request.Headers.TryGetValue("X-Forwarded-For", out var header))
        {
            return null;
        }

        var value = header.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var firstHop = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstHop) ? null : firstHop;
    }
}
