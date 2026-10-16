using System.Text.RegularExpressions;

namespace SqlAnywhereMcp.Models;

/// <summary>A stored procedure that call_procedure may call, written as owner.procedure in config.json.</summary>
public sealed partial record AllowedProcedure(string Owner, string Name)
{
    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)$")]
    private static partial Regex QualifiedName();

    /// <summary>Parses an owner.procedure entry; both parts must be plain identifiers.</summary>
    public static Result<AllowedProcedure> Parse(string entry)
    {
        var match = QualifiedName().Match(entry ?? string.Empty);
        return match.Success
            ? new Ok<AllowedProcedure>(new(match.Groups[1].Value, match.Groups[2].Value))
            : new Err<AllowedProcedure>(
                $"AllowedProcedures entry '{entry}' must be owner.procedure, e.g. dba.my_procedure, "
                    + "using only letters, digits and underscores.",
                ErrorKind.Config
            );
    }

    public override string ToString() => $"{Owner}.{Name}";
}
