namespace NWFM.Tests.Infrastructure;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

/// <summary>A new instance and database per opted-in test; never consumes application connection strings.</summary>
internal sealed class OwnedLocalDb : IAsyncDisposable
{
    private readonly string _instance = "NWFM_TEST_" + Guid.NewGuid().ToString("N");
    private readonly string _database = "NWFM_TEST_" + Guid.NewGuid().ToString("N");
    private readonly string _log;
    private bool _owned;

    private OwnedLocalDb(string source)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../../.."));
        var logs = Path.Combine(root, ".work", "unit-coverage-sql-lifecycle");
        Directory.CreateDirectory(logs);
        _log = Path.Combine(logs, _instance + ".log");
    }

    public string ConnectionString
    {
        get
        {
            if (!_owned) throw new InvalidOperationException("LocalDB ownership has not been established.");
            ValidateName(_instance);
            ValidateName(_database);
            return new SqlConnectionStringBuilder
            {
                DataSource = @"(localdb)\" + _instance,
                InitialCatalog = _database,
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                Pooling = false,
                ConnectTimeout = 30,
            }.ConnectionString;
        }
    }

    public static async Task<OwnedLocalDb> CreateAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Opted-in SQL tests require Windows LocalDB.");
        var fixture = new OwnedLocalDb(SourcePath());
        if ((await fixture.InstancesAsync()).Contains(fixture._instance, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing an existing LocalDB instance.");
        await fixture.LogAsync($"INSTANCE_ABSENT_BEFORE_CREATE {fixture._instance}");
        // Establish ownership only after this process observes successful creation.
        await fixture.RunAsync("create", fixture._instance);
        fixture._owned = true;
        try
        {
            await fixture.LogAsync($"INSTANCE_CREATED {fixture._instance}");
            await fixture.RunAsync("start", fixture._instance);
            await fixture.ExecuteMasterAsync($"IF DB_ID(N'{fixture._database}') IS NOT NULL THROW 50000, 'Unexpected existing database', 1; CREATE DATABASE [{fixture._database}];");
            await fixture.LogAsync($"DATABASE_CREATED {fixture._database}");
        }
        catch (Exception creationFailure)
        {
            try { await fixture.DisposeAsync(); }
            catch (Exception cleanupFailure) { throw new AggregateException(creationFailure, cleanupFailure); }
            throw;
        }
        return fixture;
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;

    private static void ValidateName(string name)
    {
        if (!Regex.IsMatch(name, @"\ANWFM_TEST_[0-9a-f]{32}\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing a non-owned SQL identifier.");
    }

    private async Task<string> RunAsync(params string[] arguments)
    {
        ValidateName(_instance);
        ValidateName(_database);
        var start = new ProcessStartInfo("SqlLocalDB.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start SqlLocalDB.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // This handle belongs to the process just started by this fixture.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await LogAsync($"LocalDB {string.Join(' ', arguments)} timed out.\n{await stdout}\n{await stderr}");
            throw;
        }
        var output = await stdout;
        await LogAsync($"LocalDB {string.Join(' ', arguments)}\n{output}\n{await stderr}\nExitCode={process.ExitCode}");
        if (process.ExitCode != 0) throw new InvalidOperationException($"Owned LocalDB command failed; see {_log}");
        return output;
    }

    private async Task<string[]> InstancesAsync() =>
        (await RunAsync("info")).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task ExecuteMasterAsync(string sql)
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync();
    }

    private async Task LogAsync(string message)
    {
        var evidence = $"{DateTime.UtcNow:O} instance={_instance} database={_database}\n{message}\n";
        await File.AppendAllTextAsync(_log, evidence);
        Console.WriteLine(evidence);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_owned) return;
        var failures = new List<Exception>();
        try
        {
            await ExecuteMasterAsync($"IF DB_ID(N'{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END; IF DB_ID(N'{_database}') IS NOT NULL THROW 50000, 'Owned database remains', 1;");
            await LogAsync($"DATABASE_ABSENT {_database}");
        }
        catch (Exception failure) { failures.Add(failure); }
        try { await RunAsync("stop", _instance, "-k"); }
        catch (Exception failure) { failures.Add(failure); }
        try { await RunAsync("delete", _instance); }
        catch (Exception failure) { failures.Add(failure); }
        try
        {
            if ((await InstancesAsync()).Contains(_instance, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Owned instance remains: {_instance}");
            await LogAsync($"INSTANCE_ABSENT_AFTER_CLEANUP {_instance}");
        }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException(failures);
        _owned = false;
    }
}
