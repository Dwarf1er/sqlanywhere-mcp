using System.ComponentModel;
using SqlAnywhereMcp.Auth;
using Xunit;

namespace SqlAnywhereMcp.Tests;

public class CredentialSourceTests
{
    [Theory]
    [InlineData("my-helper get db --refresh", new[] { "my-helper", "get", "db", "--refresh" })]
    [InlineData("  my-helper   get  db  ", new[] { "my-helper", "get", "db" })]
    [InlineData(
        "\"C:\\Program Files\\helper\\my-helper.exe\" get db",
        new[] { "C:\\Program Files\\helper\\my-helper.exe", "get", "db" }
    )]
    [InlineData("sh -c 'echo secret'", new[] { "sh", "-c", "echo secret" })]
    [InlineData("cmd \"\"", new[] { "cmd", "" })]
    public void CommandLineSplitHandlesQuotesAndSpaces(string commandLine, string[] expected)
    {
        Assert.Equal(expected, CommandLine.Split(commandLine));
    }

    [Fact]
    public void ReturnsStdoutWithoutTrailingNewlineAndPassesArguments()
    {
        string? program = null;
        IReadOnlyList<string>? arguments = null;
        var source = new CredentialSource(
            (p, a, _) =>
            {
                program = p;
                arguments = a;
                return new CommandResult(0, "s3cret\r\n");
            }
        );

        Assert.Equal(
            "s3cret",
            Assert.IsType<Ok<string>>(source.GetPassword("my-helper get db --refresh")).Value
        );
        Assert.Equal("my-helper", program);
        Assert.Equal(["get", "db", "--refresh"], arguments);
    }

    [Fact]
    public void RunsTheCommandOnEveryCallSoRotatedPasswordsArePickedUp()
    {
        var calls = 0;
        var source = new CredentialSource(
            (_, _, _) =>
            {
                calls++;
                return new CommandResult(0, $"pw{calls}");
            }
        );

        Assert.Equal(
            "pw1",
            Assert.IsType<Ok<string>>(source.GetPassword("my-helper get db")).Value
        );
        Assert.Equal(
            "pw2",
            Assert.IsType<Ok<string>>(source.GetPassword("my-helper get db")).Value
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void NonZeroExitCodesBecomeErrors(int exitCode)
    {
        var source = new CredentialSource((_, _, _) => new CommandResult(exitCode, string.Empty));

        var err = Assert.IsType<Err<string>>(source.GetPassword("my-helper get db --refresh"));

        Assert.Contains(
            $"'my-helper get db --refresh' failed with exit code {exitCode}",
            err.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(ErrorKind.Credential, err.Kind);
    }

    [Fact]
    public void OtherCommandsGetAGenericMessage()
    {
        var source = new CredentialSource((_, _, _) => new CommandResult(2, string.Empty));

        var err = Assert.IsType<Err<string>>(source.GetPassword("pass show db"));

        Assert.Contains(
            "'pass show db' failed with exit code 2",
            err.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void EmptyOutputIsAnError()
    {
        var source = new CredentialSource((_, _, _) => new CommandResult(0, "\n"));

        var err = Assert.IsType<Err<string>>(source.GetPassword("my-helper get db"));

        Assert.Contains("printed nothing", err.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingProgramSaysToCheckInstallation()
    {
        var source = new CredentialSource(
            (_, _, _) => throw new Win32Exception(2, "No such file or directory")
        );

        var err = Assert.IsType<Err<string>>(source.GetPassword("my-helper get db"));

        Assert.Contains("installed and on PATH", err.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyCommandIsAnError()
    {
        var source = new CredentialSource((_, _, _) => new CommandResult(0, "pw"));

        Assert.IsType<Err<string>>(source.GetPassword("   "));
    }

    [Fact]
    public void RunsARealProcess()
    {
        var command = OperatingSystem.IsWindows() ? "cmd /c echo hello" : "echo hello";

        Assert.Equal(
            "hello",
            Assert.IsType<Ok<string>>(new CredentialSource().GetPassword(command)).Value.Trim()
        );
    }

    [Fact]
    public void RealProcessDoesNotReadOurStdin()
    {
        // sqlanywhere-mcp's stdin carries the MCP protocol, so the child gets a closed stdin and sees end of input.
        var command = OperatingSystem.IsWindows()
            ? "powershell -NoProfile -Command \"[Console]::In.ReadToEnd().Length; 'done'\""
            : "sh -c 'cat; echo done'";

        Assert.EndsWith(
            "done",
            Assert.IsType<Ok<string>>(new CredentialSource().GetPassword(command)).Value.Trim()
        );
    }
}
