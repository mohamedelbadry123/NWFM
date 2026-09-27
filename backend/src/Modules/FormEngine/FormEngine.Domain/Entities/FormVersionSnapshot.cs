namespace FormEngine.Domain.Entities;

/// <summary>
/// The output for one target client that <see cref="FormDefinition.Publish"/> freezes into a
/// <see cref="FormVersion"/> row.
/// </summary>
public sealed record FormVersionSnapshot(
    string TargetClient,
    string SchemaJson,
    string SnapshotJson);
