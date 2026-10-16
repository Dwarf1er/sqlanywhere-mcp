using SqlAnywhereMcp.Config;
using SqlAnywhereMcp.Models;

namespace SqlAnywhereMcp.Database;

/// <summary>A configured database as list_databases reports it.</summary>
public sealed record DatabaseInfo(
    string Name,
    string Host,
    int? Port,
    string Database,
    string? Username,
    IReadOnlyList<string> AllowedProcedures
);

/// <summary>The configured databases, the default one and the last config.json reload error.</summary>
public sealed record DatabasesResult(
    string ConfigPath,
    string? DefaultDatabase,
    string? ReloadError,
    IReadOnlyList<DatabaseInfo> Databases
);

/// <summary>
/// The databases from config.json, each with its own client and connection, picked by name on every tool
/// call. The file is checked before each call: a valid change replaces the clients, so the next call uses
/// the new settings without restarting sqlanywhere-mcp; an invalid one is reported and the last valid configuration
/// stays in use.
/// </summary>
public sealed class DatabaseRegistry : IDisposable
{
    private readonly string _path;
    private readonly Func<string, Result<RootConfig>> _read;

    // Tool calls share the read lock; swapping the clients takes the write lock, so a client is never
    // disposed while a call is using it.
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly object _reloadGate = new();

    private RootConfig _config;
    private Dictionary<string, DatabaseClient> _clients;
    private IReadOnlyList<DatabaseConfig> _databases;
    private (DateTime Written, long Length)? _stamp;
    private string? _reloadError;

    public DatabaseRegistry(string path, RootConfig config)
        : this(path, config, ConfigService.Read) { }

    internal DatabaseRegistry(string path, RootConfig config, Func<string, Result<RootConfig>> read)
    {
        _path = path;
        _read = read;
        _config = config;
        _databases = config.Effective();
        _clients = Build(_databases);
        _stamp = Stamp(path);
    }

    /// <summary>
    /// Runs <paramref name="work"/> with the client of <paramref name="name"/>, or of the default database when
    /// it is null, after picking up any change to config.json. Throws a tool error when there is no such database.
    /// </summary>
    public T Use<T>(string? name, CancellationToken ct, Func<DatabaseSession, T> work)
    {
        ReloadIfChanged();

        _lock.EnterReadLock();
        try
        {
            var client = DatabaseClient.Unwrap(Resolve(name));
            return work(new DatabaseSession(client, ct));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>The configured databases, the default one and the last reload error, for list_databases.</summary>
    public DatabasesResult Describe()
    {
        ReloadIfChanged();

        _lock.EnterReadLock();
        try
        {
            return new DatabasesResult(
                _path,
                DefaultName(),
                _reloadError,
                _databases
                    .Select(db => new DatabaseInfo(
                        db.Name,
                        db.Host,
                        db.Port,
                        db.Database,
                        db.Username,
                        db.AllowedProcedures
                    ))
                    .ToList()
            );
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>The client for <paramref name="name"/>, or for the default database when it is null.</summary>
    internal Result<DatabaseClient> Resolve(string? name)
    {
        // Allowed, so another tool can take the last database out of the file and sqlanywhere-mcp stops reaching it.
        if (_databases.Count == 0)
            return new Err<DatabaseClient>(
                $"No database is configured. Add one under \"databases\" in {_path}.",
                ErrorKind.Usage
            );

        var names = string.Join(", ", _databases.Select(db => db.Name));
        var wanted = string.IsNullOrWhiteSpace(name) ? DefaultName() : name;
        if (wanted is null)
            return new Err<DatabaseClient>(
                $"Several databases are configured and none is the default. Pass database as one of: {names}.",
                ErrorKind.Usage
            );

        if (_clients.TryGetValue(wanted, out var client))
            return new Ok<DatabaseClient>(client);

        var note = _reloadError is null
            ? ""
            : $" config.json has an error, so the last valid version is still in use: {_reloadError}";
        return new Err<DatabaseClient>(
            $"Unknown database '{wanted}'. Configured: {names}.{note}",
            ErrorKind.Usage
        );
    }

    private string? DefaultName() =>
        _databases.Count == 1 ? _databases[0].Name : _config.DefaultDatabase;

    /// <summary>Re-reads config.json when its timestamp or size changed since the last check.</summary>
    private void ReloadIfChanged()
    {
        lock (_reloadGate)
        {
            var stamp = Stamp(_path);
            if (stamp == _stamp)
                return;
            _stamp = stamp;

            var read = stamp is null
                ? new Err<RootConfig>($"{_path} no longer exists.", ErrorKind.Config)
                : _read(_path);
            if (read is not Ok<RootConfig> loaded)
            {
                _reloadError = ((Err<RootConfig>)read).Message;
                Console.Error.WriteLine(
                    $"sqlanywhere-mcp: kept the last valid configuration. {_reloadError}"
                );
                return;
            }

            var databases = loaded.Value.Effective();
            var clients = Build(databases);

            _lock.EnterWriteLock();
            try
            {
                foreach (var old in _clients.Values)
                    old.Dispose();
                _config = loaded.Value;
                _databases = databases;
                _clients = clients;
                _reloadError = null;
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            Console.Error.WriteLine($"sqlanywhere-mcp: reloaded {_path}.");
        }
    }

    private static Dictionary<string, DatabaseClient> Build(
        IEnumerable<DatabaseConfig> databases
    ) =>
        databases.ToDictionary(
            db => db.Name,
            db => new DatabaseClient(db),
            StringComparer.OrdinalIgnoreCase
        );

    private static (DateTime, long)? Stamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? (file.LastWriteTimeUtc, file.Length) : null;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _lock.Dispose();
    }
}
