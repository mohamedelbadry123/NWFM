namespace FormEngine.Application.Forms.Models;

/// <summary>A version as the history dialog lists it — without its schema.</summary>
public sealed class FormVersionSummaryDto
{
    public Guid Id { get; init; }
    public int VersionNo { get; init; }
    public string TargetClient { get; init; } = default!;
    public string? PublishedBy { get; init; }
    public DateTime PublishedAt { get; init; }
}
