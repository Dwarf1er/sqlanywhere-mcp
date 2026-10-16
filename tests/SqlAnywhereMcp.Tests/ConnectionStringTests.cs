using SqlAnywhereMcp.Models;
using Xunit;

namespace SqlAnywhereMcp.Tests;

public class ConnectionStringTests
{
    private static DatabaseConfig Database(string? username = "u", string? appInfo = null) =>
        new()
        {
            Host = "h",
            Database = "d",
            Username = username,
            AppInfo = appInfo,
        };

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData("a b", "a b")]
    [InlineData("p;w", "{p;w}")]
    [InlineData("p=w", "{p=w}")]
    [InlineData("p{w", "{p{w}")]
    [InlineData("p}w", "{p}}w}")]
    [InlineData("a;b}c{d=e", "{a;b}}c{d=e}")]
    [InlineData(" lead", "{ lead}")]
    [InlineData("trail ", "{trail }")]
    public void QuoteEnclosesValuesThatNeedIt(string value, string expected)
    {
        Assert.Equal(expected, DatabaseConfig.Quote(value));
    }

    [Fact]
    public void PlainValuesAreWrittenAsTheyAre()
    {
        Assert.Equal(
            "Driver={SQL Anywhere 17};Host=h:2638;DBN=d;UID=u;PWD=secret",
            Database().ToConnectionString("secret")
        );
    }

    [Fact]
    public void ASemicolonOrBraceInThePasswordDoesNotBreakTheConnectionString()
    {
        Assert.Equal(
            "Driver={SQL Anywhere 17};Host=h:2638;DBN=d;UID=u;PWD={pa;ss}}word}",
            Database().ToConnectionString("pa;ss}word")
        );
    }

    [Fact]
    public void EveryValueIsQuoted()
    {
        var database = new DatabaseConfig
        {
            Host = "h;x",
            Database = "d=1",
            Username = "u;v",
            AppInfo = "my;app",
            Driver = "dri}ver",
        };

        Assert.Equal(
            "Driver={dri}}ver};Host={h;x:2638};DBN={d=1};UID={u;v};PWD=p;AppInfo={my;app}",
            database.ToConnectionString("p")
        );
    }
}
