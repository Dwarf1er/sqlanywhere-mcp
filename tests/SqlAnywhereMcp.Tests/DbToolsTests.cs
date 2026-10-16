using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlAnywhereMcp.Database;
using SqlAnywhereMcp.Tools;
using Xunit;

namespace SqlAnywhereMcp.Tests;

public class DbToolsTests
{
    [Theory]
    [InlineData("id_projet", @"id\_projet")]
    [InlineData("100%", @"100\%")]
    [InlineData("[a-z]", @"\[a-z]")]
    [InlineData(@"c:\temp", @"c:\\temp")]
    [InlineData("plain", "plain")]
    public void EscapeLikeMatchesTextLiterally(string text, string expected)
    {
        Assert.Equal(expected, DbTools.EscapeLike(text));
    }

    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(DbTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    [Fact]
    public void EveryToolIsAnnotatedAndOnlyCallProcedureIsNotReadOnly()
    {
        foreach (var method in ToolMethods())
        {
            var tool = method.GetCustomAttribute<McpServerToolAttribute>()!;
            var readOnly = method.Name != nameof(DbTools.CallProcedure);
            Assert.Equal(readOnly, tool.ReadOnly);
            Assert.Equal(!readOnly, tool.Destructive);
            Assert.Equal(readOnly, tool.Idempotent);
            Assert.False(tool.OpenWorld, method.Name);
        }
    }

    [Fact]
    public void EveryToolAndParameterHasADescription()
    {
        foreach (var method in ToolMethods())
        {
            Assert.NotNull(method.GetCustomAttribute<DescriptionAttribute>());
            foreach (
                var parameter in method
                    .GetParameters()
                    .Where(p => p.ParameterType != typeof(CancellationToken))
            )
                Assert.True(
                    parameter.GetCustomAttribute<DescriptionAttribute>() is not null,
                    $"{method.Name}.{parameter.Name}"
                );
        }
    }

    [Fact]
    public void EveryToolReturnsStructuredContentAndTakesACancellationToken()
    {
        foreach (var method in ToolMethods())
        {
            Assert.True(
                method.GetCustomAttribute<McpServerToolAttribute>()!.UseStructuredContent,
                method.Name
            );
            if (method.Name != nameof(DbTools.ListDatabases))
                Assert.Contains(
                    method.GetParameters(),
                    p => p.ParameterType == typeof(CancellationToken)
                );
        }
    }

    [Fact]
    public void RowValuesOfEveryDriverTypeSerialize()
    {
        var row = new Dictionary<string, object?>
        {
            ["text"] = "a",
            ["number"] = 12.5m,
            ["when"] = new DateTime(2026, 1, 2, 3, 4, 5),
            ["time"] = new TimeSpan(1, 2, 3),
            ["bytes"] = new byte[] { 1, 2 },
            ["nothing"] = null,
            ["flag"] = true,
        };

        var json = JsonSerializer.Serialize(
            new QueryResult([row], false, 1),
            McpJsonUtilities.DefaultOptions
        );

        Assert.Contains("\"rows\":[{", json, StringComparison.Ordinal);
        Assert.Contains("\"number\":12.5", json, StringComparison.Ordinal);
        Assert.Contains("\"truncated\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"nothing\":null", json, StringComparison.Ordinal);
    }
}
