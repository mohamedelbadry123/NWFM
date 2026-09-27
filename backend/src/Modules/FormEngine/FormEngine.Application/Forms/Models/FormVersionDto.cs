namespace FormEngine.Application.Forms.Models;

/// <summary>A published version with its frozen schema — what a fill page or a pinned task renders.</summary>
public sealed class FormVersionDto
{
    public Guid Id { get; init; }
    public Guid FormDefinitionId { get; init; }
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string FormStatus { get; init; } = default!;
    public int VersionNo { get; init; }
    public int? CurrentVersionNo { get; init; }
    public string TargetClient { get; init; } = default!;
    public string? PublishedBy { get; init; }
    public DateTime PublishedAt { get; init; }

    /// <summary>The form-builder document exactly as it was published.</summary>
    public string SchemaJson { get; init; } = "{}";

    /// <summary>The form's code, names and category at publish time.</summary>
    public string SnapshotJson { get; init; } = "{}";

    /// <summary>Whether the form still takes new submissions (published version, not deprecated or archived).</summary>
    public bool AcceptsSubmissions { get; init; }
}
