using SqlAnywhereMcp.Database;
using Xunit;

namespace SqlAnywhereMcp.Tests;

public class TableDefinitionTests
{
    private const string Definition =
        "CREATE TABLE \"DBA\".\"t\" (\n    \"id\" integer NOT NULL\n)\n;\n"
        + "COMMENT ON COLUMN \"DBA\".\"t\".\"id\" IS \n\t'Identifier'\n;\n"
        + "GRANT SELECT ON \"DBA\".\"t\" TO \"reader\"\n;\n"
        + "create trigger \"tai_t\" after insert on \"DBA\".\"t\"\r\nbegin\r\nend\n;\n"
        + "COMMENT TO PRESERVE FORMAT ON TRIGGER \"DBA\".\"t\".\"tai_t\" IS \n{create trigger tai_t}\n;\n";

    [Fact]
    public void DefaultsDropTriggersAndComments()
    {
        var filtered = DatabaseClient.FilterTableDefinition(Definition, false, false);

        Assert.Contains("CREATE TABLE", filtered, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("COMMENT", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("trigger", filtered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TriggersKeepTheirPreservedSourceWithoutColumnComments()
    {
        var filtered = DatabaseClient.FilterTableDefinition(Definition, true, false);

        Assert.Contains("create trigger \"tai_t\"", filtered, StringComparison.Ordinal);
        Assert.Contains(
            "COMMENT TO PRESERVE FORMAT ON TRIGGER",
            filtered,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("COMMENT ON COLUMN", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentsWithoutTriggers()
    {
        var filtered = DatabaseClient.FilterTableDefinition(Definition, false, true);

        Assert.Contains("COMMENT ON COLUMN", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("trigger", filtered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IncludingEverythingReturnsTheDefinitionUnchanged()
    {
        Assert.Equal(Definition, DatabaseClient.FilterTableDefinition(Definition, true, true));
    }
}
