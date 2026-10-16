using System.Text.Json;
using System.Text.Json.Serialization;
using SqlAnywhereMcp.Models;

namespace SqlAnywhereMcp.Config;

/// <summary>Loads and validates the JSON configuration file, creating a default if absent.</summary>
internal static class ConfigService
{
    // Unknown fields are errors: a misspelled "allowedProcedures" must not silently allow nothing,
    // and a misspelled "passwordCommand" must not silently leave a database without credentials.
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    /// <summary>The path given by SQLANYWHERE_MCP_CONFIG, or the default location.</summary>
    public static string FilePath =>
        Environment.GetEnvironmentVariable("SQLANYWHERE_MCP_CONFIG") ?? ConfigPaths.ConfigPath;

    /// <summary>Loads the configuration from <paramref name="path"/>, creating a template when it is missing.</summary>
    public static Result<RootConfig> Load(string path)
    {
        if (!File.Exists(path))
        {
            CreateDefault(path);
            return new Err<RootConfig>(
                $"Configuration file created at:{Environment.NewLine}  {path}{Environment.NewLine}"
                    + $"Fill in your database connection details and restart sqlanywhere-mcp.",
                ErrorKind.Config
            );
        }

        return Read(path);
    }

    /// <summary>Reads and validates <paramref name="path"/>, without creating it. Used to reload a changed file.</summary>
    public static Result<RootConfig> Read(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Err<RootConfig>(
                $"Failed to read configuration at {path}: {ex.Message}",
                ErrorKind.Config
            );
        }

        return Parse(json, path);
    }

    /// <summary>Deserializes and validates the configuration JSON read from <paramref name="path"/>.</summary>
    internal static Result<RootConfig> Parse(string json, string path)
    {
        RootConfig? config;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (HoldsPassword(doc.RootElement))
                return new Err<RootConfig>(
                    $"A \"password\" field in {path} is not supported. Remove it and set "
                        + "\"passwordCommand\" instead: a command that prints the password.",
                    ErrorKind.Config
                );

            config = doc.Deserialize<RootConfig>(ReadOptions);
        }
        catch (JsonException ex)
        {
            return new Err<RootConfig>(
                $"Failed to parse configuration at {path}: {ex.Message}",
                ErrorKind.Config
            );
        }

        if (config is null)
            return new Err<RootConfig>(
                $"Failed to parse configuration at {path}.",
                ErrorKind.Config
            );

        return Validate(config, path);
    }

    /// <summary>True when the top level, the defaults or any database has a "password" field.</summary>
    private static bool HoldsPassword(JsonElement root)
    {
        static bool Has(JsonElement element) =>
            element.ValueKind == JsonValueKind.Object
            && element
                .EnumerateObject()
                .Any(p => p.Name.Equals("password", StringComparison.OrdinalIgnoreCase));

        static IEnumerable<JsonElement> Children(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object
                ? element
                    .EnumerateObject()
                    .Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Value)
                : [];

        var databases = Children(root, "databases")
            .Where(d => d.ValueKind == JsonValueKind.Object)
            .SelectMany(d => d.EnumerateObject().Select(p => p.Value));

        return Has(root) || Children(root, "defaults").Any(Has) || databases.Any(Has);
    }

    /// <summary>Checks the databases, the default database and every allowlisted procedure.</summary>
    private static Result<RootConfig> Validate(RootConfig config, string path)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, db) in config.Databases)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new Err<RootConfig>(
                    $"A database in {path} has an empty name.",
                    ErrorKind.Config
                );
            if (!names.Add(name))
                return new Err<RootConfig>(
                    $"Two databases in {path} are named '{name}', ignoring case.",
                    ErrorKind.Config
                );
            if (string.IsNullOrWhiteSpace(db.Host) || string.IsNullOrWhiteSpace(db.Database))
                return new Err<RootConfig>(
                    $"The database '{name}' in {path} needs both \"host\" and \"database\".",
                    ErrorKind.Config
                );
            if (
                string.IsNullOrWhiteSpace(db.Username)
                && string.IsNullOrWhiteSpace(config.Defaults.Username)
            )
                return new Err<RootConfig>(
                    $"The database '{name}' in {path} has no \"username\". Set it on the database or in \"defaults\".",
                    ErrorKind.Config
                );
            if (
                string.IsNullOrWhiteSpace(db.PasswordCommand)
                && string.IsNullOrWhiteSpace(config.Defaults.PasswordCommand)
            )
                return new Err<RootConfig>(
                    $"The database '{name}' in {path} has no \"passwordCommand\": a command that prints the "
                        + "password on stdout. Set it on the database or in \"defaults\".",
                    ErrorKind.Config
                );
        }

        if (config.DefaultDatabase is { } fallback && !names.Contains(fallback))
            return new Err<RootConfig>(
                $"\"defaultDatabase\" is '{fallback}', which is not in \"databases\" in {path}.",
                ErrorKind.Config
            );

        foreach (
            var entry in config.AllowedProcedures.Concat(
                config.Databases.Values.SelectMany(db => db.AllowedProcedures)
            )
        )
            if (AllowedProcedure.Parse(entry) is Err<AllowedProcedure> err)
                return err.As<RootConfig>();

        return new Ok<RootConfig>(config);
    }

    /// <summary>Writes a template configuration file to the specified path.</summary>
    private static void CreateDefault(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written by hand so the template shows only the fields worth filling in.
        File.WriteAllText(
            path,
            """
            {
              "defaults": {
                "username": "your-username",
                "passwordCommand": "keyturn get your-secret --refresh"
              },
              "allowedProcedures": [],
              "databases": {
                "yourdb": {
                  "host": "localhost",
                  "database": "yourdb",
                  "allowedProcedures": []
                }
              }
            }

            """
        );
    }
}
