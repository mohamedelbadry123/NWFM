using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FormEngine.Infrastructure.Submissions;

/// <summary>
/// Loads the editable native-SQL statements for form submissions from
/// <c>sql/form-submissions.sql.json</c> (copied next to the app) and reloads them when the file
/// changes. Keeps SQL text out of C# so it can be tuned without a redeploy.
/// </summary>
public sealed class SqlStatementStore : IDisposable
{
    private const string RelativePath = "sql/form-submissions.sql.json";

    /// <summary>Keys starting with this are notes for whoever edits the file, not statements.</summary>
    private const char CommentPrefix = '_';

    /// <summary>Editors often fire several change events; a short delay lets the write settle.</summary>
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(150);

    private readonly string _filePath;
    private readonly ILogger<SqlStatementStore> _logger;
    private readonly FileSystemWatcher? _watcher;

    private volatile IReadOnlyDictionary<string, string> _statements = new ConcurrentDictionary<string, string>();

    public SqlStatementStore(ILogger<SqlStatementStore> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, RelativePath);

        Load();

        var directory = Path.GetDirectoryName(_filePath);
        if (directory is not null && Directory.Exists(directory))
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_filePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += (_, _) => SafeReload();
            _watcher.Created += (_, _) => SafeReload();
        }
    }

    /// <summary>The statement for <paramref name="key"/>, or throws when it is missing.</summary>
    public string Get(string key)
    {
        if (_statements.TryGetValue(key, out var sql) && !string.IsNullOrWhiteSpace(sql))
        {
            return sql;
        }

        throw new InvalidOperationException($"SQL statement '{key}' was not found in '{RelativePath}'.");
    }

    private void SafeReload()
    {
        try
        {
            Thread.Sleep(ReloadDelay);
            Load();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload form-submission SQL statements from {Path}.", _filePath);
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogWarning("Form-submission SQL statement file not found at {Path}.", _filePath);
            return;
        }

        var json = File.ReadAllText(_filePath);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

        var statements = new ConcurrentDictionary<string, string>(
            parsed.Where(kvp => !kvp.Key.StartsWith(CommentPrefix)),
            StringComparer.Ordinal);

        _statements = statements;
        _logger.LogInformation("Loaded {Count} form-submission SQL statements from {Path}.", statements.Count, _filePath);
    }

    public void Dispose() => _watcher?.Dispose();
}
