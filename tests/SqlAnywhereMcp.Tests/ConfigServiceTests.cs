using SqlAnywhereMcp.Config;
using SqlAnywhereMcp.Models;
using Xunit;

namespace SqlAnywhereMcp.Tests;

public class ConfigServiceTests
{
    private const string TwoDatabases = """
        {
          "defaults": { "username": "dba", "passwordCommand": "get-pw default", "appInfo": "my-app" },
          "allowedProcedures": ["dba.everywhere"],
          "defaultDatabase": "demo",
          "databases": {
            "demo": { "host": "localhost", "database": "demodb" },
            "staging": {
              "host": "db.example.com",
              "port": 2700,
              "database": "proddb",
              "username": "reader",
              "passwordCommand": "get-pw staging",
              "allowedProcedures": ["dba.only_here"]
            }
          }
        }
        """;

    private static RootConfig Parse(string json) =>
        Assert.IsType<Ok<RootConfig>>(ConfigService.Parse(json, "config.json")).Value;

    private static Err<RootConfig> ParseError(string json) =>
        Assert.IsType<Err<RootConfig>>(ConfigService.Parse(json, "config.json"));

    [Fact]
    public void DatabasesUseTheDefaultsTheyDoNotOverride()
    {
        var databases = Parse(TwoDatabases).Effective();

        var demo = databases.Single(db => db.Name == "demo");
        Assert.Equal("dba", demo.Username);
        Assert.Equal(2638, demo.Port);
        Assert.Equal("get-pw default", demo.PasswordCommand);
        Assert.Equal(["dba.everywhere"], demo.AllowedProcedures);
        Assert.Equal(
            "Driver={SQL Anywhere 17};Host=localhost:2638;DBN=demodb;UID=dba;PWD=pw;AppInfo=my-app",
            demo.ToConnectionString("pw")
        );

        var staging = databases.Single(db => db.Name == "staging");
        Assert.Equal("reader", staging.Username);
        Assert.Equal(2700, staging.Port);
        Assert.Equal("get-pw staging", staging.PasswordCommand);
        Assert.Equal(["dba.everywhere", "dba.only_here"], staging.AllowedProcedures);
    }

    [Theory]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"password":"x","databases":{"a":{"host":"h","database":"a"}}}"""
    )]
    [InlineData("""{"defaults":{"Password":"x"},"databases":{"a":{"host":"h","database":"a"}}}""")]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"databases":{"a":{"host":"h","database":"a","password":"x"}}}"""
    )]
    public void StoredPasswordIsRejected(string json)
    {
        var err = ParseError(json);

        Assert.Contains("passwordCommand", err.Message, StringComparison.Ordinal);
        Assert.Equal(ErrorKind.Config, err.Kind);
    }

    [Theory]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"databases":{"a":{"host":"h"}}}""",
        "needs both"
    )]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"databases":{"a":{"host":"h","database":"a"},"A":{"host":"h","database":"b"}}}""",
        "ignoring case"
    )]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"defaultDatabase":"nope","databases":{"a":{"host":"h","database":"a"}}}""",
        "defaultDatabase"
    )]
    public void InvalidDatabasesAreRejected(string json, string expected)
    {
        Assert.Contains(expected, ParseError(json).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"allowedProcedures":["my_proc"],"databases":{"a":{"host":"h","database":"a"}}}"""
    )]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"databases":{"a":{"host":"h","database":"a","allowedProcedures":["dba.my proc"]}}}"""
    )]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"allowedProcedures":["dba.\"x\""],"databases":{"a":{"host":"h","database":"a"}}}"""
    )]
    public void AllowedProceduresMustBeOwnerQualifiedIdentifiers(string json)
    {
        Assert.Contains("owner.procedure", ParseError(json).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"allowedProcedure":["dba.p"],"databases":{"a":{"host":"h","database":"a"}}}"""
    )]
    [InlineData(
        """{"defaults":{"username":"u","passwordCommand":"p"},"databases":{"a":{"host":"h","database":"a","passwordComand":"x"}}}"""
    )]
    [InlineData("""{"host":"h","database":"a"}""")]
    public void UnknownFieldsAreRejected(string json)
    {
        Assert.Contains("Failed to parse", ParseError(json).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """{"defaults":{"passwordCommand":"p"},"databases":{"a":{"host":"h","database":"a"}}}""",
        "username"
    )]
    [InlineData(
        """{"defaults":{"username":"u"},"databases":{"a":{"host":"h","database":"a"}}}""",
        "passwordCommand"
    )]
    public void UsernameAndPasswordCommandAreRequired(string json, string expected)
    {
        Assert.Contains(expected, ParseError(json).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateNamesAreRejected()
    {
        ParseError(
            """{"defaults":{"username":"u","passwordCommand":"p"},"databases":{"a":{"host":"h","database":"a"},"a":{"host":"h","database":"b"}}}"""
        );
    }

    [Fact]
    public void MalformedJsonIsAnError()
    {
        Assert.Contains("config.json", ParseError("{ not json").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDriverDefaultsToSqlAnywhere17AndCanBeOverridden()
    {
        var databases = Parse(
                """
                {
                  "defaults": { "username": "u", "passwordCommand": "p", "driver": "SQL Anywhere 17" },
                  "databases": {
                    "a": { "host": "h", "database": "a" },
                    "b": { "host": "h", "database": "b", "driver": "/opt/sqlanywhere17/lib64/libdbodbc17.so" }
                  }
                }
                """
            )
            .Effective();

        Assert.StartsWith(
            "Driver={SQL Anywhere 17};",
            databases.Single(db => db.Name == "a").ToConnectionString("pw"),
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "Driver={/opt/sqlanywhere17/lib64/libdbodbc17.so};",
            databases.Single(db => db.Name == "b").ToConnectionString("pw"),
            StringComparison.Ordinal
        );
        Assert.StartsWith(
            "Driver={SQL Anywhere 17};",
            new DatabaseConfig { Host = "h", Database = "d" }.ToConnectionString("pw"),
            StringComparison.Ordinal
        );
    }
}
