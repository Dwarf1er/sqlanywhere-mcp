using ModelContextProtocol;
using SqlAnywhereMcp.Database;
using Xunit;

namespace SqlAnywhereMcp.Tests;

public class ReadOnlyQueryTests
{
    [Theory]
    [InlineData("select 1")]
    [InlineData("  WITH t AS (select 1 x) select * from t")]
    [InlineData("-- note\nselect 'a;b'")]
    public void ReadOnlyQueriesAreAccepted(string sql)
    {
        Assert.Equal(
            sql,
            Assert.IsType<Ok<string>>(DatabaseClient.ValidateReadOnlyQuery(sql)).Value
        );
    }

    [Theory]
    [InlineData("delete from t", "Only SELECT and WITH")]
    [InlineData("/* select */ delete from t", "Only SELECT and WITH")]
    [InlineData("select 1; delete from t", "Multi-statement")]
    [InlineData("select * into t2 from t", "INTO")]
    [InlineData("select 1 into #tmp", "INTO")]
    [InlineData("with c as (select 1 x) select x\ninto t2 from c", "INTO")]
    public void OtherQueriesAreRejected(string sql, string expected)
    {
        var err = Assert.IsType<Err<string>>(DatabaseClient.ValidateReadOnlyQuery(sql));

        Assert.Contains(expected, err.Message, StringComparison.Ordinal);
        Assert.Equal(ErrorKind.Usage, err.Kind);
    }

    [Theory]
    [InlineData("select 'into' from t")]
    [InlineData("select \"into\" from t")]
    [InlineData("select [into] from t")]
    [InlineData("select intolerance, into_x from t")]
    public void IntoInsideLiteralsOrIdentifiersIsAccepted(string sql)
    {
        Assert.IsType<Ok<string>>(DatabaseClient.ValidateReadOnlyQuery(sql));
    }

    [Fact]
    public void FailuresAreThrownAsToolErrors()
    {
        var ex = Assert.Throws<McpException>(() =>
            DatabaseClient.Unwrap(new Err<string>("boom", ErrorKind.Usage))
        );

        Assert.Equal("boom", ex.Message);
    }
}
