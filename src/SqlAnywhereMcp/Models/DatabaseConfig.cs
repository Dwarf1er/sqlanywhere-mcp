using System.Text.Json.Serialization;

namespace SqlAnywhereMcp.Models;

/// <summary>
/// One database sqlanywhere-mcp can connect to, an entry of "databases" in config.json keyed by its name. Fields left
/// out use <see cref="RootConfig.Defaults"/>, and allowedProcedures adds to the top-level list.
/// </summary>
public sealed record DatabaseConfig
{
    /// <summary>The name tools take as their database argument: the entry's key in config.json.</summary>
    [JsonIgnore]
    public string Name { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;
    public string Database { get; init; } = string.Empty;
    public int? Port { get; init; }
    public string? Username { get; init; }

    /// <summary>Command that prints this database's password on stdout. Null uses the default one.</summary>
    public string? PasswordCommand { get; init; }
    public string? AppInfo { get; init; }

    /// <summary>ODBC driver name or driver library path. Null uses the default driver.</summary>
    public string? Driver { get; init; }

    /// <summary>Procedures that call_procedure may call on this database, on top of the top-level ones.</summary>
    public IReadOnlyList<string> AllowedProcedures { get; init; } = [];

    /// <summary>Builds the ODBC connection string using this configuration and the supplied password.</summary>
    public string ToConnectionString(string password)
    {
        var port = Port ?? ConnectionDefaults.DefaultPort;
        var host = port > 0 ? $"{Host}:{port}" : Host;
        var driver = Driver ?? ConnectionDefaults.DefaultDriver;
        var conn =
            $"Driver={{{driver.Replace("}", "}}")}}};Host={Quote(host)};DBN={Quote(Database)};UID={Quote(Username ?? string.Empty)};PWD={Quote(password)}";
        return string.IsNullOrEmpty(AppInfo) ? conn : $"{conn};AppInfo={Quote(AppInfo)}";
    }

    /// <summary>
    /// Encloses a connection string value in braces when it holds a character that would otherwise end or
    /// change it (<c>;</c>, <c>{</c>, <c>}</c>, <c>=</c>) or has leading or trailing spaces, doubling any
    /// closing brace inside. Other values are left as they are.
    /// </summary>
    internal static string Quote(string value) =>
        value.Length > 0
        && (
            value.IndexOfAny([';', '{', '}', '=']) >= 0
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1])
        )
            ? "{" + value.Replace("}", "}}") + "}"
            : value;
}
