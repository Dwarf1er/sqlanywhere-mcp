namespace SqlAnywhereMcp.Models;

/// <summary>Values every database uses unless it sets its own.</summary>
public sealed record ConnectionDefaults
{
    /// <summary>SQL Anywhere's standard TCP port.</summary>
    public const int DefaultPort = 2638;

    /// <summary>The ODBC driver name SQL Anywhere 17 registers on Windows.</summary>
    public const string DefaultDriver = "SQL Anywhere 17";

    public string? Username { get; init; }

    /// <summary>Credential helper: a command that prints the database password on stdout. Required, here or per database.</summary>
    public string? PasswordCommand { get; init; }
    public string? AppInfo { get; init; }

    /// <summary>ODBC driver name as registered with the driver manager, or the path of the driver library. Null uses <see cref="DefaultDriver"/>.</summary>
    public string? Driver { get; init; }
    public int Port { get; init; } = DefaultPort;
}

/// <summary>Represents the JSON configuration file: the databases sqlanywhere-mcp can connect to, keyed by name.</summary>
public sealed record RootConfig
{
    public ConnectionDefaults Defaults { get; init; } = new();

    /// <summary>Procedures that call_procedure may call on every database, as owner.procedure. Empty allows none.</summary>
    public IReadOnlyList<string> AllowedProcedures { get; init; } = [];

    /// <summary>The database a tool uses when called without one. Not needed with a single database.</summary>
    public string? DefaultDatabase { get; init; }

    public IReadOnlyDictionary<string, DatabaseConfig> Databases { get; init; } =
        new Dictionary<string, DatabaseConfig>();

    /// <summary>
    /// Every database, named after its key, with <see cref="Defaults"/> filled in and the top-level procedures
    /// added, so a <see cref="DatabaseConfig"/> is complete on its own.
    /// </summary>
    public IReadOnlyList<DatabaseConfig> Effective() =>
        Databases
            .Select(entry =>
                entry.Value with
                {
                    Name = entry.Key,
                    Port = entry.Value.Port ?? Defaults.Port,
                    Username = string.IsNullOrEmpty(entry.Value.Username)
                        ? Defaults.Username
                        : entry.Value.Username,
                    PasswordCommand = string.IsNullOrWhiteSpace(entry.Value.PasswordCommand)
                        ? Defaults.PasswordCommand
                        : entry.Value.PasswordCommand,
                    AppInfo = entry.Value.AppInfo ?? Defaults.AppInfo,
                    Driver = string.IsNullOrWhiteSpace(entry.Value.Driver)
                        ? Defaults.Driver
                        : entry.Value.Driver,
                    AllowedProcedures = AllowedProcedures
                        .Concat(entry.Value.AllowedProcedures)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                }
            )
            .ToList();
}
