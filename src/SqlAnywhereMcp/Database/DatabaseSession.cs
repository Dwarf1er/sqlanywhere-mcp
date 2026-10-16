using System.Data.Odbc;

namespace SqlAnywhereMcp.Database;

/// <summary>
/// One tool call's view of a <see cref="DatabaseClient"/>: the same operations, all stopped when the call's
/// cancellation token is cancelled.
/// </summary>
public sealed class DatabaseSession(DatabaseClient client, CancellationToken cancellationToken)
{
    /// <summary>The settings of the database this call uses.</summary>
    public Models.DatabaseConfig Config => client.Config;

    /// <summary>Runs a trusted internal query with hardcoded SQL and optional parameters.</summary>
    public QueryResult ExecuteQuery(
        string sql,
        int maxRows = 500,
        params OdbcParameter[] parameters
    ) => client.ExecuteQuery(sql, maxRows, cancellationToken, parameters);

    public QueryResult ExecuteUserQuery(string sql, int maxRows = 500) =>
        client.ExecuteUserQuery(sql, maxRows, cancellationToken);

    public PlanResult ExplainUserQuery(string sql) =>
        client.ExplainUserQuery(sql, cancellationToken);

    public QueryResult DescribeUserQuery(string sql) =>
        client.DescribeUserQuery(sql, cancellationToken);

    public QueryResult GetTableDefinition(
        string tableName,
        string? owner,
        bool includeTriggers,
        bool includeComments
    ) =>
        client.GetTableDefinition(
            tableName,
            owner,
            includeTriggers,
            includeComments,
            cancellationToken
        );

    public QueryResult ExecuteCountQuery(string tableName, string? owner = null) =>
        client.ExecuteCountQuery(tableName, owner, cancellationToken);

    public QueryResult CallProcedure(string procName, Dictionary<string, string> args) =>
        client.CallProcedure(procName, args, cancellationToken);
}
