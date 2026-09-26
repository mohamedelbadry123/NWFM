using FormEngine.Application.Uploads.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Uploads.Commands.UploadFormFile;

/// <summary>
/// Stores one picked media file straight away, before the form is submitted. The row starts
/// <c>PENDING</c> with no submission; submitting the form links it. Files never travel inside the
/// submit payload.
/// </summary>
[Authorize(Policy = NwfmPolicies.FormUploaders)]
public sealed record UploadFormFileCommand : IRequest<Result<UploadedFileDto>>
{
    public Guid FormDefinitionId { get; init; }

    /// <summary>The version being filled, so the file is checked against the schema in force.</summary>
    public int? VersionNo { get; init; }

    /// <summary>The field (<c>data_name</c>) the file was picked for.</summary>
    public string DataName { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    /// <summary>Size reported by the request; the stored size is re-measured after writing.</summary>
    public long SizeBytes { get; init; }

    /// <summary>What the fill belongs to, e.g. a work item. Set together with <see cref="ContextId"/>.</summary>
    public string? ContextType { get; init; }

    public string? ContextId { get; init; }

    /// <summary>Request body stream — read once by the handler, never buffered into memory.</summary>
    public Stream Content { get; init; } = Stream.Null;
}
