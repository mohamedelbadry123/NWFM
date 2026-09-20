using Microsoft.AspNetCore.Http;

namespace FormEngine.Api.Contracts;

/// <summary>Body of a form create. The code is set once and never changes afterwards.</summary>
public sealed record CreateFormRequest(string Code, string NameEn, string NameAr, string Category, string? DepartmentCode);

/// <summary>Body of a form update — main info only; the schema has its own endpoint.</summary>
public sealed record UpdateFormRequest(string NameEn, string NameAr, string Category, string? DepartmentCode);

/// <summary>Body of a schema save: the form-builder document.</summary>
public sealed record SaveFormSchemaRequest(string SchemaJson);

public sealed record CloneFormRequest(string NewCode, string NewNameEn, string NewNameAr);

/// <summary>
/// Multipart body of a media upload. The file is streamed to storage rather than buffered, so it
/// arrives as <see cref="IFormFile"/> and is opened once.
/// </summary>
public sealed class UploadFormFileRequest
{
    public IFormFile? File { get; init; }

    public Guid FormDefinitionId { get; init; }

    /// <summary>The version being filled, so the file is checked against the schema in force.</summary>
    public int? VersionNo { get; init; }

    /// <summary>The field (<c>data_name</c>) the file was picked for.</summary>
    public string DataName { get; init; } = string.Empty;

    public string? ContextType { get; init; }

    public string? ContextId { get; init; }
}

/// <summary>Body of a submit: the answers, plus what the fill belongs to and how to recognise a retry.</summary>
public sealed record SubmitFormRequest
{
    public int? VersionNo { get; init; }
    public string? ContextType { get; init; }
    public string? ContextId { get; init; }
    public Guid? ClientSubmissionId { get; init; }
    public DateTimeOffset? ClientFilledAt { get; init; }
    public Dictionary<string, object?> Answers { get; init; } = [];
}
