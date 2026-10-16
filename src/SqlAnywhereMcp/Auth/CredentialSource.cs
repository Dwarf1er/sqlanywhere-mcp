using System.ComponentModel;
using System.Diagnostics;

namespace SqlAnywhereMcp.Auth;

/// <summary>Exit code and standard output of a finished password command.</summary>
internal sealed record CommandResult(int ExitCode, string Output);

/// <summary>
/// Gets the database password from a credential helper: the configured password command, run without a
/// shell, whose standard output is the password. Nothing is stored on disk, and the command runs again for
/// every new connection so a rotated password is picked up. The helper's standard error is passed through
/// to sqlanywhere-mcp's own, where MCP clients show it in their logs.
/// </summary>
internal sealed class CredentialSource(
    Func<string, IReadOnlyList<string>, CancellationToken, CommandResult>? run = null
)
{
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, CommandResult> _run =
        run ?? RunProcess;

    /// <summary>Runs the command and returns the password it printed, or an actionable error.</summary>
    public Result<string> GetPassword(string command, CancellationToken ct = default)
    {
        var parts = CommandLine.Split(command ?? string.Empty);
        if (parts.Count == 0)
            return new Err<string>(
                "The password command is empty. Set \"passwordCommand\" in config.json to a command that prints the password.",
                ErrorKind.Credential
            );

        CommandResult result;
        try
        {
            result = _run(parts[0], parts.Skip(1).ToList(), ct);
        }
        catch (Win32Exception ex)
        {
            return new Err<string>(
                $"Could not run the password command '{command}': {ex.Message}. "
                    + "Check that the program is installed and on PATH, or change \"passwordCommand\" in config.json.",
                ErrorKind.Credential
            );
        }
        catch (InvalidOperationException ex)
        {
            return new Err<string>(
                $"Could not run the password command '{command}': {ex.Message}",
                ErrorKind.Credential
            );
        }

        if (result.ExitCode != 0)
            return new Err<string>(
                $"The password command '{command}' failed with exit code {result.ExitCode}. "
                    + "Its own error output, if any, is in the MCP client's log for sqlanywhere-mcp.",
                ErrorKind.Credential
            );

        var password = result.Output.TrimEnd('\r', '\n');
        if (password.Length == 0)
            return new Err<string>(
                $"The password command '{command}' printed nothing.",
                ErrorKind.Credential
            );

        return new Ok<string>(password);
    }

    /// <summary>
    /// Runs the command with stdout captured and stdin closed, since sqlanywhere-mcp's own stdin and stdout carry the
    /// MCP protocol. stderr is inherited so the command's diagnostics reach the MCP client's logs.
    /// </summary>
    private static CommandResult RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process =
            Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        // Cancelling stops the helper, which also ends the read below.
        using var cancellation = ct.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
        });

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        ct.ThrowIfCancellationRequested();

        return new CommandResult(process.ExitCode, output);
    }
}
