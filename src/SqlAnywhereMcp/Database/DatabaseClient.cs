using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ModelContextProtocol;
using SqlAnywhereMcp.Auth;
using SqlAnywhereMcp.Models;

namespace SqlAnywhereMcp.Database;

/// <summary>Rows returned by a query, capped at the requested maximum.</summary>
public sealed record QueryResult(List<Dictionary<string, object?>> Rows, bool Truncated, int Count);

/// <summary>A GRAPHICAL_PLAN() operator tree with the optimizer's estimates.</summary>
public sealed record PlanResult(
    string Query,
    Dictionary<string, object?>? Plan,
    string? EstimatedRows,
    string? EstimatedTime
);

/// <summary>Manages a persistent ODBC connection to a SQL Anywhere database and exposes query execution methods.</summary>
public sealed class DatabaseClient : IDisposable
{
    private const int MaxQueryLength = 1_048_576;
    private const int MaxRowsLimit = 10_000;
    private const int CommandTimeoutSeconds = 60;

    private readonly DatabaseConfig _config;
    private readonly CredentialSource _credentials = new();
    private OdbcConnection? _connection;
    private readonly object _reconnectLock = new();

    public DatabaseClient(DatabaseConfig config) => _config = config;

    /// <summary>The settings this client connects with, including the procedures it may call.</summary>
    public DatabaseConfig Config => _config;

    /// <summary>Executes a user-provided SQL query after enforcing read-only validation.</summary>
    public QueryResult ExecuteUserQuery(string sql, int maxRows, CancellationToken ct) =>
        Unwrap(ValidateReadOnlyQuery(sql).AndThen(query => ExecuteInternal(query, maxRows, ct)));

    /// <summary>Validates a user-provided query then returns a compact JSON operator tree via GRAPHICAL_PLAN().</summary>
    public PlanResult ExplainUserQuery(string sql, CancellationToken ct) =>
        Unwrap(
            ValidateReadOnlyQuery(sql)
                .AndThen(query =>
                    ExecuteInternal(
                        "SELECT GRAPHICAL_PLAN(?)",
                        1,
                        ct,
                        new OdbcParameter("sql", query)
                    )
                )
                .AndThen(PlanXml)
                .AndThen(xml => ParseGraphicalPlan(sql, xml))
        );

    /// <summary>Validates a user-provided query then describes its result set via sa_describe_query() without running it.</summary>
    public QueryResult DescribeUserQuery(string sql, CancellationToken ct) =>
        Unwrap(
            ValidateReadOnlyQuery(sql)
                .AndThen(query =>
                    ExecuteInternal(
                        """
                        select column_number,
                               name,
                               domain_name_with_size as data_type,
                               nulls_allowed,
                               base_owner_name,
                               base_table_name,
                               base_column_name
                          from sa_describe_query(?)
                         order by column_number
                        """,
                        maxRows: 1000,
                        ct,
                        new OdbcParameter("sql", query)
                    )
                )
        );

    /// <summary>Returns sa_get_table_definition() for each matching table, optionally without triggers and comments.</summary>
    public QueryResult GetTableDefinition(
        string tableName,
        string? owner,
        bool includeTriggers,
        bool includeComments,
        CancellationToken ct
    )
    {
        var parameters = new List<OdbcParameter> { new("tableName", tableName) };
        var ownerFilter = "";
        if (owner != null)
        {
            ownerFilter = "\n   and sysuser.user_name = ?";
            parameters.Add(new OdbcParameter("owner", owner));
        }

        return Unwrap(
            ExecuteInternal(
                    $"""
                    select sysuser.user_name as owner,
                           systab.table_name,
                           sa_get_table_definition(sysuser.user_name, systab.table_name) as definition
                      from sys.systab,
                           sys.sysuser
                     where systab.creator = sysuser.user_id
                       and systab.table_type_str in ('BASE', 'MAT VIEW', 'GBL TEMP')
                       and systab.table_name = ?{ownerFilter}
                    """,
                    maxRows: 10,
                    ct,
                    parameters.ToArray()
                )
                .Map(result =>
                {
                    foreach (var row in result.Rows)
                        if (row["definition"] is string definition)
                            row["definition"] = FilterTableDefinition(
                                definition,
                                includeTriggers,
                                includeComments
                            );
                    return result;
                })
        );
    }

    /// <summary>
    /// Drops trigger and comment statements from sa_get_table_definition() output, which separates
    /// statements with a line holding only a semicolon. "COMMENT TO PRESERVE FORMAT ON TRIGGER" holds a
    /// trigger's original source, so it goes with the triggers rather than the comments.
    /// </summary>
    internal static string FilterTableDefinition(
        string definition,
        bool includeTriggers,
        bool includeComments
    )
    {
        const string separator = "\n;\n";

        bool Keep(string statement)
        {
            var text = statement.TrimStart();
            var isTrigger =
                text.StartsWith("create trigger", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith(
                    "comment to preserve format on trigger",
                    StringComparison.OrdinalIgnoreCase
                );
            var isComment = text.StartsWith("comment on ", StringComparison.OrdinalIgnoreCase);

            return (includeTriggers || !isTrigger) && (includeComments || !isComment);
        }

        var kept = definition.Split(separator).Where(Keep);
        return string.Join(separator, kept);
    }

    /// <summary>Extracts the plan XML from the single-row, single-column GRAPHICAL_PLAN() result.</summary>
    private static Result<string> PlanXml(QueryResult result)
    {
        if (result.Rows.Count == 0)
            return new Err<string>("No plan returned");

        var xml = result.Rows[0].Values.FirstOrDefault() as string;
        return string.IsNullOrEmpty(xml) ? new Err<string>("Empty plan") : new Ok<string>(xml);
    }

    /// <summary>Parses a GRAPHICAL_PLAN() XML string into a compact operator tree.</summary>
    private static Result<PlanResult> ParseGraphicalPlan(string originalSql, string xml)
    {
        XDocument xdoc;
        try
        {
            xdoc = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return new Err<PlanResult>("Failed to parse plan XML");
        }

        var root = xdoc.Root!;
        var rootOp = root.Elements("query").FirstOrDefault()?.Elements("operator").FirstOrDefault();
        var costEl = root.Element("cost");
        return new Ok<PlanResult>(
            new PlanResult(
                originalSql,
                rootOp != null ? BuildPlanNode(rootOp) : null,
                (string?)costEl?.Attribute("EstimatedNumRows"),
                (string?)costEl?.Attribute("EstimatedTotalTime")
            )
        );
    }

    /// <summary>Recursively converts a GRAPHICAL_PLAN operator element into a dictionary node.</summary>
    private static Dictionary<string, object?> BuildPlanNode(XElement op)
    {
        var children = op.Elements("operator").Select(BuildPlanNode).ToList();
        var node = new Dictionary<string, object?>
        {
            ["op"] = (string?)op.Attribute("lname") ?? (string?)op.Attribute("name") ?? "?",
            ["type"] = (string?)op.Attribute("ltype"),
        };
        if (children.Count > 0)
            node["children"] = children;
        return node;
    }

    /// <summary>Executes a trusted internal query with hardcoded SQL and optional parameters.</summary>
    public QueryResult ExecuteQuery(
        string sql,
        int maxRows,
        CancellationToken ct,
        params OdbcParameter[] parameters
    ) => Unwrap(ExecuteInternal(sql, maxRows, ct, parameters));

    /// <summary>The value of a successful result. A failure throws, so the MCP client sees a tool error (isError) with the message.</summary>
    internal static T Unwrap<T>(Result<T> result) =>
        result switch
        {
            Ok<T> ok => ok.Value,
            Err<T> err => throw new McpException(err.Message),
            _ => throw new InvalidOperationException("Unhandled Result variant"),
        };

    /// <summary>Core execution path; opens a reader and collects up to maxRows rows.</summary>
    private Result<QueryResult> ExecuteInternal(
        string sql,
        int maxRows,
        CancellationToken ct,
        params OdbcParameter[] parameters
    ) =>
        Connect(ct)
            .AndThen(connection =>
                Odbc(
                    () =>
                    {
                        maxRows = Math.Clamp(maxRows, 1, MaxRowsLimit);
                        using var cmd = new OdbcCommand(sql, connection)
                        {
                            CommandTimeout = CommandTimeoutSeconds,
                        };
                        foreach (var p in parameters)
                            cmd.Parameters.Add(p);
                        // Cancelling the token asks the server to stop the statement.
                        using var cancellation = ct.Register(cmd.Cancel);
                        using var reader = cmd.ExecuteReader();

                        var rows = new List<Dictionary<string, object?>>();
                        while (reader.Read() && rows.Count < maxRows)
                        {
                            ct.ThrowIfCancellationRequested();
                            var row = new Dictionary<string, object?>();
                            for (int i = 0; i < reader.FieldCount; i++)
                                row[reader.GetName(i)] = reader.IsDBNull(i)
                                    ? null
                                    : reader.GetValue(i);
                            rows.Add(row);
                        }

                        var truncated = rows.Count == maxRows && reader.Read();
                        return new QueryResult(rows, truncated, rows.Count);
                    },
                    ct
                )
            );

    /// <summary>Runs an ODBC operation, turning driver and connection-state errors into an Err.</summary>
    private static Result<T> Odbc<T>(Func<T> operation, CancellationToken ct)
    {
        try
        {
            return new Ok<T>(operation());
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // Cancelling a statement makes the driver throw; report it as the cancellation it is.
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
            return new Err<T>(ex.Message);
        }
        catch (Exception ex)
            when (ex is DllNotFoundException
                || ex is TypeInitializationException { InnerException: DllNotFoundException }
            )
        {
            return new Err<T>(
                "The ODBC driver manager could not be loaded. On Linux and macOS, install unixODBC "
                    + "(libodbc), then the SQL Anywhere 17 ODBC driver. "
                    + ex.GetBaseException().Message
            );
        }
    }

    /// <summary>Returns the open ODBC connection, reconnecting automatically if it was dropped.</summary>
    private Result<OdbcConnection> Connect(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_reconnectLock)
        {
            if (_connection?.State == ConnectionState.Open)
            {
                var ping = Odbc(
                    () =>
                    {
                        using var cmd = new OdbcCommand("SELECT 1", _connection);
                        return cmd.ExecuteScalar();
                    },
                    ct
                );
                if (ping.IsOk)
                    return new Ok<OdbcConnection>(_connection);

                _connection.Dispose();
                _connection = null;
            }

            return BuildConnectionString(ct)
                .AndThen(connectionString =>
                    Odbc(
                        () =>
                        {
                            var conn = new OdbcConnection(connectionString);
                            try
                            {
                                conn.Open();
                            }
                            catch (DbException ex)
                            {
                                conn.Dispose();
                                throw new InvalidOperationException(
                                    $"Could not connect to '{_config.Name}' with ODBC driver "
                                        + $"'{_config.Driver ?? ConnectionDefaults.DefaultDriver}': {ex.Message}",
                                    ex
                                );
                            }
                            catch
                            {
                                conn.Dispose();
                                throw;
                            }
                            _connection = conn;
                            return conn;
                        },
                        ct
                    )
                );
        }
    }

    /// <summary>Rejects queries that are not SELECT or WITH, exceed the size limit, or contain multiple statements.</summary>
    internal static Result<string> ValidateReadOnlyQuery(string sql)
    {
        if (sql.Length > MaxQueryLength)
            return new Err<string>(
                $"Query exceeds the {MaxQueryLength / 1024 / 1024} MB size limit.",
                ErrorKind.Usage
            );

        var stripped = StripComments(sql).TrimStart();

        if (
            !stripped.StartsWith("select", StringComparison.OrdinalIgnoreCase)
            && !stripped.StartsWith("with", StringComparison.OrdinalIgnoreCase)
        )
            return new Err<string>("Only SELECT and WITH queries are allowed.", ErrorKind.Usage);

        if (HasSemicolonOutsideLiterals(stripped))
            return new Err<string>("Multi-statement queries are not allowed.", ErrorKind.Usage);

        if (HasIntoKeywordOutsideLiterals(stripped))
            return new Err<string>(
                "SELECT ... INTO is not allowed: it creates tables or sets variables.",
                ErrorKind.Usage
            );

        return new Ok<string>(sql);
    }

    /// <summary>Removes SQL single-line and block comments while preserving string literals.</summary>
    private static string StripComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        int i = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '\'')
            {
                sb.Append(sql[i++]);
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        sb.Append(sql[i++]);
                        if (i < sql.Length && sql[i] == '\'')
                            sb.Append(sql[i++]);
                        else
                            break;
                    }
                    else
                    {
                        sb.Append(sql[i++]);
                    }
                }
                continue;
            }
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                    i++;
                continue;
            }
            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                    i++;
                if (i + 1 < sql.Length)
                    i += 2;
                continue;
            }
            sb.Append(sql[i++]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the SQL has the keyword INTO outside string literals and quoted identifiers.
    /// INTO is reserved in SQL Anywhere, so an unquoted occurrence is always SELECT ... INTO.
    /// </summary>
    private static bool HasIntoKeywordOutsideLiterals(string sql)
    {
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (c is '\'' or '"' or '[')
            {
                var close = c == '[' ? ']' : c;
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == close)
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == close && close != ']')
                        {
                            i += 2;
                            continue;
                        }
                        break;
                    }
                    i++;
                }
                i++;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (
                    i < sql.Length
                    && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '@' or '#' or '$')
                )
                    i++;
                if (sql.AsSpan(start, i - start).Equals("into", StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            i++;
        }
        return false;
    }

    /// <summary>Returns true if the SQL contains a semicolon that is not inside a string literal.</summary>
    private static bool HasSemicolonOutsideLiterals(string sql)
    {
        bool inString = false;
        for (int i = 0; i < sql.Length; i++)
        {
            if (!inString && sql[i] == '\'')
            {
                inString = true;
                continue;
            }
            if (inString && sql[i] == '\'')
            {
                if (i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }
                inString = false;
                continue;
            }
            if (!inString && sql[i] == ';')
                return true;
        }
        return false;
    }

    /// <summary>Executes a row-count query, building a safe dynamic FROM clause after validating identifiers.</summary>
    public QueryResult ExecuteCountQuery(string tableName, string? owner, CancellationToken ct) =>
        Unwrap(
            ValidateIdentifier(tableName)
                .AndThen(_ =>
                    owner != null ? ValidateIdentifier(owner) : new Ok<string>(string.Empty)
                )
                .AndThen(_ =>
                {
                    var qualified =
                        owner != null ? $"\"{owner}\".\"{tableName}\"" : $"\"{tableName}\"";
                    return ExecuteInternal(
                        $"select count(*) as row_count from {qualified}",
                        maxRows: 1,
                        ct
                    );
                })
        );

    /// <summary>Validates that a name contains only letters, digits, and underscores.</summary>
    private static Result<string> ValidateIdentifier(string name) =>
        Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$")
            ? new Ok<string>(name)
            : new Err<string>(
                $"Identifier '{name}' contains unsupported characters. Use execute_query for complex names.",
                ErrorKind.Usage
            );

    /// <summary>Calls a procedure listed in AllowedProcedures in config.json, passing named arguments only.</summary>
    public QueryResult CallProcedure(
        string procName,
        Dictionary<string, string> args,
        CancellationToken ct
    ) =>
        Unwrap(
            ResolveAllowedProcedure(_config.AllowedProcedures, procName)
                .AndThen(procedure =>
                {
                    foreach (var key in args.Keys)
                        if (ValidateIdentifier(key) is Err<string> err)
                            return err.As<QueryResult>();

                    var argList = string.Join(", ", args.Keys.Select(k => $"\"{k}\" = ?"));
                    var parameters = args.Select(kv => new OdbcParameter(kv.Key, kv.Value))
                        .ToArray();
                    return ExecuteInternal(
                        $"call \"{procedure.Owner}\".\"{procedure.Name}\"({argList})",
                        maxRows: 1,
                        ct,
                        parameters
                    );
                })
        );

    /// <summary>
    /// Finds the AllowedProcedures entry for <paramref name="procName"/>, given as procedure or owner.procedure.
    /// A bare name must match exactly one entry.
    /// </summary>
    internal static Result<AllowedProcedure> ResolveAllowedProcedure(
        IReadOnlyList<string> allowedProcedures,
        string procName
    )
    {
        var allowed = allowedProcedures
            .Select(AllowedProcedure.Parse)
            .OfType<Ok<AllowedProcedure>>()
            .Select(ok => ok.Value)
            .ToList();

        if (allowed.Count == 0)
            return new Err<AllowedProcedure>(
                "No procedures are allowed. Add owner.procedure entries to AllowedProcedures in config.json.",
                ErrorKind.Usage
            );

        var matches = allowed
            .Where(p =>
                procName.Contains('.')
                    ? p.ToString().Equals(procName, StringComparison.OrdinalIgnoreCase)
                    : p.Name.Equals(procName, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        return matches.Count switch
        {
            1 => new Ok<AllowedProcedure>(matches[0]),
            0 => new Err<AllowedProcedure>(
                $"'{procName}' is not in AllowedProcedures. Allowed: {string.Join(", ", allowed)}.",
                ErrorKind.Usage
            ),
            _ => new Err<AllowedProcedure>(
                $"'{procName}' matches several owners. Pass one of: {string.Join(", ", matches)}.",
                ErrorKind.Usage
            ),
        };
    }

    /// <summary>Builds the ODBC connection string with a fresh password from the configured password command.</summary>
    private Result<string> BuildConnectionString(CancellationToken ct) =>
        _credentials
            .GetPassword(_config.PasswordCommand ?? string.Empty, ct)
            .Map(_config.ToConnectionString);

    public void Dispose() => _connection?.Dispose();
}
