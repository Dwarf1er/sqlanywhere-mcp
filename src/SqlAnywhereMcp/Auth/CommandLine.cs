using System.Text;

namespace SqlAnywhereMcp.Auth;

/// <summary>Splits a configured command line into a program and its arguments.</summary>
internal static class CommandLine
{
    /// <summary>
    /// Splits a command line into the program and its arguments. Whitespace separates
    /// arguments; single or double quotes group text containing spaces.
    /// </summary>
    public static IReadOnlyList<string> Split(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        var parts = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        char? quote = null;

        foreach (var c in commandLine)
        {
            if (quote is not null)
            {
                if (c == quote)
                    quote = null;
                else
                    current.Append(c);

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                inToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
            }
            else
            {
                current.Append(c);
                inToken = true;
            }
        }

        if (inToken)
            parts.Add(current.ToString());

        return parts;
    }
}
