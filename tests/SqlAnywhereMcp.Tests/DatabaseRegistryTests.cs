using ModelContextProtocol;
using SqlAnywhereMcp.Config;
using SqlAnywhereMcp.Database;
using SqlAnywhereMcp.Models;
using Xunit;

namespace SqlAnywhereMcp.Tests;

public sealed class DatabaseRegistryTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "sqlanywhere-mcp-registry-tests-" + Guid.NewGuid().ToString("N")
    );

    public DatabaseRegistryTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string ConfigPath => Path.Combine(_folder, "config.json");

    private static string Config(string defaultDatabase, params string[] names) =>
        $$"""
            {
              "defaults": { "username": "u", "passwordCommand": "p" },
              "defaultDatabase": {{(defaultDatabase.Length == 0 ? "null" : $"\"{defaultDatabase}\"")}},
              "databases": {{{string.Join(
                ",",
                names.Select(n => $"\"{n}\":{{\"host\":\"localhost\",\"database\":\"{n}\"}}")
            )}}}
            }
            """;

    /// <summary>Writes the file and moves its timestamp forward, so a rewrite within the same tick is still seen.</summary>
    private void Write(string json)
    {
        var previous = File.Exists(ConfigPath)
            ? File.GetLastWriteTimeUtc(ConfigPath)
            : DateTime.UtcNow;
        File.WriteAllText(ConfigPath, json);
        File.SetLastWriteTimeUtc(ConfigPath, previous.AddSeconds(2));
    }

    private DatabaseRegistry Registry(string json)
    {
        Write(json);
        var config = Assert.IsType<Ok<RootConfig>>(ConfigService.Read(ConfigPath)).Value;
        return new DatabaseRegistry(ConfigPath, config);
    }

    /// <summary>The name of the database a call would use, or the error it would return.</summary>
    private static string Picked(DatabaseRegistry registry, string? name)
    {
        try
        {
            return registry.Use(name, CancellationToken.None, db => db.Config.Name);
        }
        catch (McpException ex)
        {
            return ex.Message;
        }
    }

    [Fact]
    public void ASingleDatabaseIsTheDefault()
    {
        using var registry = Registry(Config("", "demo"));

        Assert.Equal("demo", Picked(registry, null));
    }

    [Fact]
    public void ANamedDatabaseIsPickedRegardlessOfCase()
    {
        using var registry = Registry(Config("demo", "demo", "staging"));

        Assert.Equal("staging", Picked(registry, "STAGING"));
        Assert.Equal("demo", Picked(registry, null));
    }

    [Fact]
    public void WithoutADefaultTheErrorListsTheDatabases()
    {
        using var registry = Registry(Config("", "demo", "staging"));

        Assert.Contains("demo, staging", Picked(registry, null), StringComparison.Ordinal);
        Assert.Contains("Unknown database", Picked(registry, "nope"), StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingTheLastDatabaseStopsReachingIt()
    {
        using var registry = Registry(Config("", "demo"));

        Write("""{"databases":{}}""");

        Assert.Contains(
            "No database is configured",
            Picked(registry, null),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void AChangedFileIsUsedOnTheNextCall()
    {
        using var registry = Registry(Config("", "demo"));

        Write(Config("", "other"));

        Assert.Equal("other", Picked(registry, null));
    }

    [Fact]
    public void AnInvalidChangeKeepsTheLastValidConfiguration()
    {
        using var registry = Registry(Config("", "demo"));

        Write("{ not json");

        Assert.Equal("demo", Picked(registry, null));
        Assert.Contains(
            "Failed to parse",
            registry.Describe().ReloadError,
            StringComparison.Ordinal
        );

        Write(Config("", "fixed"));

        Assert.Equal("fixed", Picked(registry, null));
        Assert.Null(registry.Describe().ReloadError);
    }

    [Fact]
    public void ADeletedFileKeepsTheLastValidConfiguration()
    {
        using var registry = Registry(Config("", "demo"));

        File.Delete(ConfigPath);

        Assert.Equal("demo", Picked(registry, null));
        Assert.Contains(
            "no longer exists",
            registry.Describe().ReloadError,
            StringComparison.Ordinal
        );
    }
}
