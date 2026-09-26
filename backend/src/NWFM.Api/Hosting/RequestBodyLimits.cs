using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NWFM.Shared.Options;

namespace NWFM.Api.Hosting;

/// <summary>
/// Raises the Kestrel, IIS and multipart ceilings to match <c>FileStorage:MaxFileSizeMb</c>.
/// </summary>
/// <remarks>
/// Kestrel's default is 30,000,000 bytes (~28.6 MB), and setting
/// <c>Kestrel__Limits__MaxRequestBodySize</c> in configuration does not apply it — the host loader
/// ignores that key. Without this, a video at the configured cap is refused while the request body is
/// being read, before the upload handler can answer with its own message. IIS in-process ignores
/// Kestrel's limit, so both servers are set from the same number.
/// </remarks>
public static class RequestBodyLimits
{
    private const int BytesPerMb = 1024 * 1024;

    /// <summary>Extra megabytes so a file at the configured cap plus its multipart fields still fits.</summary>
    private const int MultipartHeadroomMb = 20;

    public static void AddRequestBodyLimits(this IHostApplicationBuilder builder)
    {
        var storageMb = builder.Configuration.GetValue(
            $"{FileStorageOptions.SectionName}:{nameof(FileStorageOptions.MaxFileSizeMb)}",
            new FileStorageOptions().MaxFileSizeMb);

        var maxBytes = (long)(storageMb + MultipartHeadroomMb) * BytesPerMb;

        if (builder is WebApplicationBuilder web)
        {
            web.WebHost.ConfigureKestrel((_, kestrel) => kestrel.Limits.MaxRequestBodySize = maxBytes);
        }

        builder.Services.Configure<KestrelServerOptions>(kestrel => kestrel.Limits.MaxRequestBodySize = maxBytes);
        builder.Services.Configure<IISServerOptions>(iis => iis.MaxRequestBodySize = maxBytes);
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = maxBytes);
    }
}
