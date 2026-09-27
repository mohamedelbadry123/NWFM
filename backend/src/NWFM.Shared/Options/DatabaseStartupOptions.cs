namespace NWFM.Shared.Options;

/// <summary>
/// Binds the DatabaseStartup configuration section that controls what the API
/// does to the database when it starts. All flags default to false so a missing
/// section is production-safe.
/// </summary>
public sealed class DatabaseStartupOptions
{
    public const string SectionName = "DatabaseStartup";

    /// <summary>Run EF Core MigrateAsync on API start.</summary>
    public bool ApplyMigrations { get; init; }

    /// <summary>Run reference-data seed on API start.</summary>
    public bool SeedData { get; init; }

    /// <summary>
    /// Execute CREATE OR ALTER scripts under sql/procedures on API start.
    /// One-off files under sql/scripts are not covered and stay manual.
    /// </summary>
    public bool ApplySqlObjects { get; init; }
}
