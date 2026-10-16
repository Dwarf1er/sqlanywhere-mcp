using SqlAnywhereMcp.Database;
using SqlAnywhereMcp.Models;
using Xunit;

namespace SqlAnywhereMcp.Tests;

public class AllowedProcedureTests
{
    private static readonly string[] Allowed = ["dba.gen_model", "dba.shared", "other.shared"];

    [Theory]
    [InlineData("gen_model", "dba", "gen_model")]
    [InlineData("GEN_MODEL", "dba", "gen_model")]
    [InlineData("other.shared", "other", "shared")]
    public void ResolvesByNameOrQualifiedName(string procName, string owner, string name)
    {
        var resolved = DatabaseClient.ResolveAllowedProcedure(Allowed, procName);

        Assert.Equal(
            new AllowedProcedure(owner, name),
            Assert.IsType<Ok<AllowedProcedure>>(resolved).Value
        );
    }

    [Fact]
    public void UnknownProcedureIsRejectedWithTheAllowedList()
    {
        var err = Assert.IsType<Err<AllowedProcedure>>(
            DatabaseClient.ResolveAllowedProcedure(Allowed, "drop_everything")
        );

        Assert.Contains(
            "dba.gen_model, dba.shared, other.shared",
            err.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(ErrorKind.Usage, err.Kind);
    }

    [Fact]
    public void OwnerMustMatchWhenQualified()
    {
        Assert.IsType<Err<AllowedProcedure>>(
            DatabaseClient.ResolveAllowedProcedure(Allowed, "other.gen_model")
        );
    }

    [Fact]
    public void AmbiguousNameAsksForTheOwner()
    {
        var err = Assert.IsType<Err<AllowedProcedure>>(
            DatabaseClient.ResolveAllowedProcedure(Allowed, "shared")
        );

        Assert.Contains("dba.shared, other.shared", err.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyAllowlistRejectsEverything()
    {
        var err = Assert.IsType<Err<AllowedProcedure>>(
            DatabaseClient.ResolveAllowedProcedure([], "gen_model")
        );

        Assert.Contains("AllowedProcedures", err.Message, StringComparison.Ordinal);
    }
}
