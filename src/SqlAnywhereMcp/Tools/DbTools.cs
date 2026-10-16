using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Odbc;
using System.Text;
using ModelContextProtocol.Server;
using SqlAnywhereMcp.Database;

namespace SqlAnywhereMcp.Tools;

[McpServerToolType]
/// <summary>Exposes SQL Anywhere database objects as MCP tools for schema exploration and query execution.</summary>
public sealed class DbTools(DatabaseRegistry databases)
{
    private const string DatabaseParameter =
        "The database to use, by name from list_databases. Leave it out to use the default database.";

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List the databases sqlanywhere-mcp can connect to, with their host, port, database name and the procedures "
            + "call_procedure may call on each. Every other tool takes one of these names as its database "
            + "argument; without it, the tool uses defaultDatabase. Also reports an error in config.json "
            + "when the last change to it could not be loaded."
    )]
    public DatabasesResult ListDatabases() => databases.Describe();

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Execute a read-only SQL query (SELECT or WITH only) and return results as JSON. "
            + "Rows are capped at maxRows (default 500). Includes a 'truncated' flag when the cap is hit. "
            + "Multi-statement queries, SELECT ... INTO and comments used to bypass restrictions are rejected."
    )]
    public QueryResult ExecuteQuery(
        [Description("A single read-only SQL statement: SELECT or WITH only.")] string sql,
        [Description("Maximum number of rows returned (default 500, at most 10000).")]
            int maxRows = 500,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) => databases.Use(database, cancellationToken, db => db.ExecuteUserQuery(sql, maxRows));

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the execution plan for a read-only SQL query (SELECT or WITH only). "
            + "Returns a compact operator tree with estimated row count and time."
    )]
    public PlanResult ExplainQuery(
        [Description("A single read-only SQL statement: SELECT or WITH only.")] string sql,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) => databases.Use(database, cancellationToken, db => db.ExplainUserQuery(sql));

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List all distinct owners (users) that own at least one table or view in the database."
    )]
    public QueryResult ListOwners(
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select distinct sysuser.user_name as owner
                      from sys.systab,
                           sys.sysuser
                     where systab.creator = sysuser.user_id
                       and systab.table_type_str in ('BASE', 'VIEW', 'MAT VIEW')
                     order by sysuser.user_name
                    """
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List all base tables and views in the database with their type and owner. "
            + "Pass owner to filter to a specific owner. "
            + "Pass nameFilter as a SQL LIKE pattern (e.g. 'cust%', '%order%') to search by name."
    )]
    public QueryResult ListTables(
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(
            "SQL LIKE pattern matched against the table or view name, e.g. 'cust%'. Optional."
        )]
            string? nameFilter = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                QueryWithFilters(
                    db,
                    """
                    select systab.table_name,
                           systab.table_type_str as table_type,
                           sysuser.user_name as owner
                      from sys.systab,
                           sys.sysuser
                     where systab.creator = sysuser.user_id
                       and systab.table_type_str in ('BASE', 'VIEW', 'MAT VIEW')
                    """,
                    "order by systab.table_name",
                    maxRows: 500,
                    ("sysuser.user_name = ?", owner),
                    ("systab.table_name like ?", nameFilter)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the column definitions of a table: name, type, length, nullability, default value, and comment. "
            + "Pass owner to disambiguate when multiple owners have a table with the same name."
    )]
    public QueryResult DescribeTable(
        [Description("Table name.")] string tableName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
            {
                if (owner == null)
                    return db.ExecuteQuery(
                        """
                        select syscolumns.cname,
                               syscolumns.coltype,
                               syscolumns.length,
                               syscolumns.nulls,
                               syscolumns.default_value,
                               syscolumns.in_primary_key,
                               syscolumns.remarks
                          from sys.syscolumns
                         where syscolumns.tname = ?
                         order by syscolumns.colno
                        """,
                        maxRows: 500,
                        new OdbcParameter("tableName", tableName)
                    );

                return db.ExecuteQuery(
                    """
                    select syscolumns.cname,
                           syscolumns.coltype,
                           syscolumns.length,
                           syscolumns.nulls,
                           syscolumns.default_value,
                           syscolumns.in_primary_key,
                           syscolumns.remarks
                      from sys.syscolumns
                     where syscolumns.tname = ?
                       and cast(syscolumns.creator as varchar(30)) = ?
                     order by syscolumns.colno
                    """,
                    maxRows: 500,
                    new OdbcParameter("tableName", tableName),
                    new OdbcParameter("owner", owner)
                );
            }
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("List all check constraints on a table.")]
    public QueryResult ListCheckConstraints(
        [Description("Table name.")] string tableName,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select sysconstraint.constraint_name,
                           syscheck.check_defn
                      from sys.sysconstraint,
                           sys.syscheck,
                           sys.systab
                     where sysconstraint.constraint_id = syscheck.check_id
                       and sysconstraint.table_object_id = systab.object_id
                       and sysconstraint.constraint_type = 'C'
                       and systab.table_name = ?
                     order by sysconstraint.constraint_name
                    """,
                    maxRows: 500,
                    new OdbcParameter("tableName", tableName)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List all indexes of a table with their type (primary key, foreign key, secondary, text), "
            + "uniqueness, and the columns they cover in order (descending columns are marked 'desc'). "
            + "Pass owner to disambiguate when multiple owners have a table with the same name."
    )]
    public QueryResult ListIndexes(
        [Description("Table name.")] string tableName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                QueryWithFilters(
                    db,
                    """
                    select sysidx.index_name,
                           case sysidx.index_category
                                when 1 then 'primary key'
                                when 2 then 'foreign key'
                                when 3 then 'secondary'
                                when 4 then 'text'
                           end as index_type,
                           sysidx."unique",
                           sysuser.user_name as owner,
                           list(systabcol.column_name || case when sysidxcol."order" = 'D' then ' desc' else '' end
                                order by sysidxcol.sequence) as columns
                      from sys.systab,
                           sys.sysuser,
                           sys.sysidx,
                           sys.sysidxcol,
                           sys.systabcol
                     where systab.creator = sysuser.user_id
                       and sysidx.table_id = systab.table_id
                       and sysidxcol.table_id = sysidx.table_id
                       and sysidxcol.index_id = sysidx.index_id
                       and systabcol.table_id = sysidxcol.table_id
                       and systabcol.column_id = sysidxcol.column_id
                    """,
                    """
                     group by sysidx.index_category,
                              sysidx.index_name,
                              sysidx."unique",
                              sysuser.user_name
                     order by sysidx.index_category,
                              sysidx.index_name
                    """,
                    maxRows: 500,
                    ("systab.table_name = ?", tableName),
                    ("sysuser.user_name = ?", owner)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List all foreign keys in the database. "
            + "Pass tableName to filter to a specific child (referencing) table."
    )]
    public QueryResult ListForeignKeys(
        [Description("Only foreign keys whose child (referencing) table has this name. Optional.")]
            string? tableName = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
            {
                const string baseSql = """
                    select sysforeignkeys.role,
                           sysforeignkeys.foreign_tname,
                           sysforeignkeys.primary_tname,
                           sysforeignkeys."columns"
                      from sys.sysforeignkeys
                    """;

                if (tableName == null)
                    return db.ExecuteQuery(
                        baseSql + "\n order by sysforeignkeys.foreign_tname, sysforeignkeys.role"
                    );

                return db.ExecuteQuery(
                    baseSql
                        + "\n where sysforeignkeys.foreign_tname = ?\n order by sysforeignkeys.role",
                    maxRows: 500,
                    new OdbcParameter("tableName", tableName)
                );
            }
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the exact row count for a table. "
            + "Table and owner names must contain only letters, digits, and underscores."
    )]
    public QueryResult GetTableStats(
        [Description("Table name.")] string tableName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) => databases.Use(database, cancellationToken, db => db.ExecuteCountQuery(tableName, owner));

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List all stored procedures and functions in the database, each tagged with its object type. "
            + "Pass objectType as 'procedure' or 'function' to filter to one type only."
    )]
    public QueryResult ListProcedures(
        [Description("Restrict to 'procedure' or 'function'. Optional.")] string? objectType = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
            {
                // The manual documents sysprocedure.is_deterministic as NULL for procedures and set for functions.
                const string baseSql = """
                    select sysprocedure.proc_name,
                           case when sysprocedure.is_deterministic is null then 'procedure' else 'function' end as object_type
                      from sys.sysprocedure
                    """;

                if (objectType == null)
                    return db.ExecuteQuery(baseSql + "\n order by sysprocedure.proc_name");

                var typeFilter = objectType.Equals("function", StringComparison.OrdinalIgnoreCase)
                    ? "sysprocedure.is_deterministic is not null"
                    : "sysprocedure.is_deterministic is null";

                return db.ExecuteQuery(
                    baseSql + $"\n where {typeFilter}\n order by sysprocedure.proc_name"
                );
            }
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("List all views in the database.")]
    public QueryResult ListViews(
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select sysviews.viewname
                      from sys.sysviews
                     order by sysviews.viewname
                    """
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("List all triggers in the database with their associated table.")]
    public QueryResult ListTriggers(
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select systriggers.trigname,
                           systriggers.tname
                      from sys.systriggers
                     order by systriggers.trigname
                    """
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get database-level properties: name, version, character set, collation, and page size."
    )]
    public QueryResult GetDatabaseInfo(
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select db_property('Name') as db_name,
                           property('ProductVersion') as version,
                           db_property('CharSet') as charset,
                           db_property('Collation') as collation,
                           db_property('PageSize') as page_size
                    """
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("List all database events (scheduled jobs) with their name and enabled status.")]
    public QueryResult ListEvents(
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select sysevent.event_name,
                           sysevent.enabled
                      from sys.sysevent
                     order by sysevent.event_name
                    """
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the full source definition of a stored procedure or function by name. "
            + "Returns the original source with its comments and formatting when the database preserved it, "
            + "otherwise the definition as reformatted by the server. "
            + "Pass owner to disambiguate when multiple owners have a procedure with the same name."
    )]
    public QueryResult GetProcedureSource(
        [Description("Procedure or function name.")] string procName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                QueryWithFilters(
                    db,
                    """
                    select sysprocedure.proc_name,
                           sysuser.user_name as owner,
                           isnull(sysprocedure.source, sysprocedure.proc_defn) as proc_defn
                      from sys.sysprocedure,
                           sys.sysuser
                     where sysprocedure.creator = sysuser.user_id
                    """,
                    "",
                    maxRows: 10,
                    ("sysprocedure.proc_name = ?", procName),
                    ("sysuser.user_name = ?", owner)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the full source definition of a view by name. "
            + "Returns the original source with its comments and formatting when the database preserved it, "
            + "otherwise the definition as reformatted by the server. "
            + "Pass owner to disambiguate when multiple owners have a view with the same name."
    )]
    public QueryResult GetViewSource(
        [Description("View name.")] string viewName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                QueryWithFilters(
                    db,
                    """
                    select systab.table_name as viewname,
                           sysuser.user_name as owner,
                           isnull(syssource.source, sysview.view_def) as viewtext
                      from sys.systab
                           join sys.sysuser on sysuser.user_id = systab.creator
                           join sys.sysview on sysview.view_object_id = systab.object_id
                           left outer join sys.syssource on syssource.object_id = systab.object_id
                     where 1 = 1
                    """,
                    "",
                    maxRows: 10,
                    ("systab.table_name = ?", viewName),
                    ("sysuser.user_name = ?", owner)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the full source definition of a trigger by name. "
            + "Returns the original source with its comments and formatting when the database preserved it, "
            + "otherwise the definition as reformatted by the server."
    )]
    public QueryResult GetTriggerSource(
        [Description("Trigger name.")] string triggerName,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select systrigger.trigger_name as trigname,
                           systab.table_name as tname,
                           isnull(systrigger.source, systrigger.trigger_defn) as trigdefn
                      from sys.systrigger,
                           sys.systab
                     where systrigger.table_id = systab.table_id
                       and systrigger.trigger_name = ?
                    """,
                    maxRows: 10,
                    new OdbcParameter("triggerName", triggerName)
                )
        );

    [McpServerTool(
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Call a stored procedure that the user allowlisted for this database in config.json "
            + "(the top-level allowedProcedures plus the database's own; list_databases shows them). "
            + "Any other procedure is rejected, and the error lists the allowed ones. "
            + "Pass procName as 'procedure' or 'owner.procedure' (needed when two owners share a name). "
            + "Pass args as the procedure's own named parameters; any parameter left out uses the "
            + "procedure's own default. Allowlisted procedures run with the connection's permissions and "
            + "may have side effects, so only call one when the user asks for what it does."
    )]
    public QueryResult CallProcedure(
        [Description("Procedure or function name.")] string procName,
        [Description(
            "The procedure's named parameters and their values. Parameters left out use the procedure's defaults."
        )]
            Dictionary<string, string> args,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) => databases.Use(database, cancellationToken, db => db.CallProcedure(procName, args));

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Get the full source definition of a database event by name.")]
    public QueryResult GetEventSource(
        [Description("Event name.")] string eventName,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select sysevent.event_name,
                           sysevent.enabled,
                           sysevent.source
                      from sys.sysevent
                     where sysevent.event_name = ?
                    """,
                    maxRows: 1,
                    new OdbcParameter("eventName", eventName)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the CREATE statements for a table, as generated by the database: the table with its columns, "
            + "keys, foreign keys, the sequences it uses, and granted privileges. "
            + "Triggers and comments are left out by default because they can make the output very large; "
            + "pass includeTriggers or includeComments to add them, or use get_trigger_source and "
            + "get_table_comments instead. "
            + "Pass owner to disambiguate when multiple owners have a table with the same name."
    )]
    public QueryResult GetTableDefinition(
        [Description("Table name.")] string tableName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description("Include the table's triggers (default false).")] bool includeTriggers = false,
        [Description(
            "Include the COMMENT ON statements for the table and its columns (default false)."
        )]
            bool includeComments = false,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db => db.GetTableDefinition(tableName, owner, includeTriggers, includeComments)
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Describe a stored procedure or function: its parameters (in, out, inout) with their types and "
            + "defaults, the columns of the result set it returns, and a function's return value. "
            + "Use it to know what to pass to call_procedure. "
            + "Pass owner to disambiguate when multiple owners have a procedure with the same name."
    )]
    public QueryResult DescribeProcedure(
        [Description("Procedure or function name.")] string procName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                QueryWithFilters(
                    db,
                    """
                    select sysuser.user_name as owner,
                           sysprocparm.parm_id,
                           sysprocparm.parm_name,
                           case sysprocparm.parm_type
                                when 0 then 'parameter'
                                when 1 then 'result column'
                                when 2 then 'sqlstate'
                                when 3 then 'sqlcode'
                                when 4 then 'return value'
                           end as parm_kind,
                           case when sysprocparm.parm_mode_in = 'Y' and sysprocparm.parm_mode_out = 'Y' then 'inout'
                                when sysprocparm.parm_mode_in = 'Y' then 'in'
                                when sysprocparm.parm_mode_out = 'Y' then 'out'
                           end as parm_mode,
                           sysprocparm.base_type_str as data_type,
                           sysprocparm."default"
                      from sys.sysprocparm,
                           sys.sysprocedure,
                           sys.sysuser
                     where sysprocparm.proc_id = sysprocedure.proc_id
                       and sysprocedure.creator = sysuser.user_id
                    """,
                    """
                     order by sysuser.user_name,
                              sysprocparm.parm_type,
                              sysprocparm.parm_id
                    """,
                    maxRows: 500,
                    ("sysprocedure.proc_name = ?", procName),
                    ("sysuser.user_name = ?", owner)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Describe the result set of a read-only SQL query (SELECT or WITH only) without running it: "
            + "each output column's name, data type, nullability, and the table column it comes from."
    )]
    public QueryResult DescribeQuery(
        [Description("A single read-only SQL statement: SELECT or WITH only.")] string sql,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) => databases.Use(database, cancellationToken, db => db.DescribeUserQuery(sql));

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List the views that depend on a table or view, directly or through other views. "
            + "Use it to see what a change to the table could affect. "
            + "Pass owner to disambiguate when multiple owners have a table with the same name."
    )]
    public QueryResult ListDependentViews(
        [Description("Table or view name.")] string tableName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select sysuser.user_name as view_owner,
                           systab.table_name as view_name
                      from sa_dependent_views(?, ?) as dependent_views,
                           sys.systab,
                           sys.sysuser
                     where systab.table_id = dependent_views.dep_view_id
                       and systab.creator = sysuser.user_id
                     order by sysuser.user_name,
                              systab.table_name
                    """,
                    maxRows: 500,
                    new OdbcParameter("tableName", tableName),
                    new OdbcParameter("owner", (object?)owner ?? DBNull.Value)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the comments (remarks, set by COMMENT ON) of a table and of each of its columns. Returns one "
            + "'table' row then one row per column. "
            + "Pass owner to disambiguate when multiple owners have a table with the same name."
    )]
    public QueryResult GetTableComments(
        [Description("Table name.")] string tableName,
        [Description(
            "Owner (schema) name, to disambiguate when several owners have an object with the same name. Optional."
        )]
            string? owner = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
            {
                var ownerFilter = owner != null ? "\n   and sysuser.user_name = ?" : "";

                var sql = $"""
                    select 'table' as level,
                           sysuser.user_name as owner,
                           cast(null as char(128)) as column_name,
                           sysremark.remarks,
                           0 as column_id
                      from sys.systab
                           join sys.sysuser on sysuser.user_id = systab.creator
                           left outer join sys.sysremark on sysremark.object_id = systab.object_id
                     where systab.table_name = ?{ownerFilter}
                    union all
                    select 'column',
                           sysuser.user_name,
                           systabcol.column_name,
                           sysremark.remarks,
                           systabcol.column_id
                      from sys.systab
                           join sys.sysuser on sysuser.user_id = systab.creator
                           join sys.systabcol on systabcol.table_id = systab.table_id
                           left outer join sys.sysremark on sysremark.object_id = systabcol.object_id
                     where systab.table_name = ?{ownerFilter}
                     order by 2, 5
                    """;

                var parameters = new List<OdbcParameter>();
                for (var branch = 0; branch < 2; branch++)
                {
                    parameters.Add(new OdbcParameter($"tableName{branch}", tableName));
                    if (owner != null)
                        parameters.Add(new OdbcParameter($"owner{branch}", owner));
                }

                return db.ExecuteQuery(sql, maxRows: 2000, parameters.ToArray());
            }
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List the web services (HTTP, SOAP, DISH, RAW, ...) defined in the database with their type, "
            + "whether they are enabled, whether they require authentication or HTTPS, and the user they run as. "
            + "Pass nameFilter as a SQL LIKE pattern (e.g. 'web%') to search by name."
    )]
    public QueryResult ListWebServices(
        [Description("SQL LIKE pattern matched against the service name, e.g. 'web%'. Optional.")]
            string? nameFilter = null,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                QueryWithFilters(
                    db,
                    """
                    select syswebservice.service_name,
                           syswebservice.service_type,
                           syswebservice.enabled,
                           syswebservice.auth_required,
                           syswebservice.secure_required,
                           sysuser.user_name as run_as
                      from sys.syswebservice
                           left outer join sys.sysuser on sysuser.user_id = syswebservice.user_id
                     where 1 = 1
                    """,
                    "order by syswebservice.service_name",
                    maxRows: 2000,
                    ("syswebservice.service_name like ?", nameFilter)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Get the definition of a web service by name: the SQL statement it runs, its URL handling, "
            + "the DISH prefix for DISH services, and its remarks."
    )]
    public QueryResult GetWebServiceSource(
        [Description("Web service name.")] string serviceName,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select syswebservice.service_name,
                           syswebservice.service_type,
                           syswebservice.enabled,
                           syswebservice.auth_required,
                           syswebservice.secure_required,
                           syswebservice.url_path,
                           sysuser.user_name as run_as,
                           syswebservice.parameter,
                           syswebservice.statement,
                           syswebservice.remarks
                      from sys.syswebservice
                           left outer join sys.sysuser on sysuser.user_id = syswebservice.user_id
                     where syswebservice.service_name = ?
                    """,
                    maxRows: 1,
                    new OdbcParameter("serviceName", serviceName)
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List all sequences in the database with their owner, start, increment, bounds, and cycling."
    )]
    public QueryResult ListSequences(
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select sysuser.user_name as owner,
                           syssequence.sequence_name,
                           syssequence.start_with,
                           syssequence.increment_by,
                           syssequence.min_value,
                           syssequence.max_value,
                           syssequence.cycle,
                           syssequence.cache
                      from sys.syssequence,
                           sys.sysuser
                     where syssequence.owner = sysuser.user_id
                     order by sysuser.user_name,
                              syssequence.sequence_name
                    """
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "List all user-defined data types (domains) with their owner, base type, nullability, default, "
            + "and check condition."
    )]
    public QueryResult ListUserTypes(
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
                db.ExecuteQuery(
                    """
                    select sysuser.user_name as owner,
                           sysusertype.type_name,
                           sysusertype.base_type_str as base_type,
                           sysusertype.nulls,
                           sysusertype."default",
                           sysusertype."check"
                      from sys.sysusertype,
                           sys.sysuser
                     where sysusertype.creator = sysuser.user_id
                     order by sysuser.user_name,
                              sysusertype.type_name
                    """
                )
        );

    [McpServerTool(
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        "Search the source code of procedures and functions, views, triggers, events, and web service "
            + "statements for a piece of text, case-insensitively, and list the objects that contain it. "
            + "The text is matched literally (% and _ are not wildcards). "
            + "Pass objectType as 'procedure', 'view', 'trigger', 'event', or 'web service' to search one kind only. "
            + "Read a match with the matching get_*_source tool."
    )]
    public QueryResult SearchSource(
        [Description(
            "Text to search for. Matched literally and case-insensitively; % and _ are not wildcards."
        )]
            string text,
        [Description(
            "Search one kind only: 'procedure', 'view', 'trigger', 'event' or 'web service'. Optional."
        )]
            string? objectType = null,
        [Description("Maximum number of objects returned (default 200, at most 10000).")]
            int maxRows = 200,
        [Description(DatabaseParameter)] string? database = null,
        CancellationToken cancellationToken = default
    ) =>
        databases.Use(
            database,
            cancellationToken,
            db =>
            {
                var typeFilter = objectType != null ? "\n   and source_code.object_type = ?" : "";
                var sql = $"""
                    select source_code.object_type,
                           source_code.owner,
                           source_code.object_name
                      from (select 'procedure' as object_type,
                                   sysuser.user_name as owner,
                                   sysprocedure.proc_name as object_name,
                                   isnull(sysprocedure.source, sysprocedure.proc_defn) as body
                              from sys.sysprocedure
                                   join sys.sysuser on sysuser.user_id = sysprocedure.creator
                            union all
                            select 'view',
                                   sysuser.user_name,
                                   systab.table_name,
                                   isnull(syssource.source, sysview.view_def)
                              from sys.systab
                                   join sys.sysuser on sysuser.user_id = systab.creator
                                   join sys.sysview on sysview.view_object_id = systab.object_id
                                   left outer join sys.syssource on syssource.object_id = systab.object_id
                            union all
                            select 'trigger',
                                   sysuser.user_name,
                                   systrigger.trigger_name,
                                   isnull(systrigger.source, systrigger.trigger_defn)
                              from sys.systrigger
                                   join sys.systab on systab.table_id = systrigger.table_id
                                   join sys.sysuser on sysuser.user_id = systab.creator
                             where systrigger.trigger_name is not null
                            union all
                            select 'event',
                                   sysuser.user_name,
                                   sysevent.event_name,
                                   isnull(sysevent.source, sysevent.action)
                              from sys.sysevent
                                   join sys.sysuser on sysuser.user_id = sysevent.creator
                            union all
                            select 'web service',
                                   sysuser.user_name,
                                   syswebservice.service_name,
                                   syswebservice.statement
                              from sys.syswebservice
                                   left outer join sys.sysuser on sysuser.user_id = syswebservice.user_id
                           ) as source_code
                     where source_code.body like ? escape '\'{typeFilter}
                     order by source_code.object_type,
                              source_code.owner,
                              source_code.object_name
                    """;

                var parameters = new List<OdbcParameter>
                {
                    new("pattern", "%" + EscapeLike(text) + "%"),
                };
                if (objectType != null)
                    parameters.Add(new OdbcParameter("objectType", objectType));

                return db.ExecuteQuery(sql, maxRows, parameters.ToArray());
            }
        );

    /// <summary>Escapes LIKE wildcards (%, _ and [ character classes) so the text matches literally with ESCAPE '\'.</summary>
    internal static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");

    /// <summary>
    /// Runs <paramref name="baseSql"/>, which must end in a where clause, with "and clause" added for each
    /// filter that has a value, then <paramref name="tail"/> (group by and order by).
    /// </summary>
    private static QueryResult QueryWithFilters(
        DatabaseSession db,
        string baseSql,
        string tail,
        int maxRows,
        params (string Clause, string? Value)[] filters
    )
    {
        var sql = new StringBuilder(baseSql.TrimEnd());
        var parameters = new List<OdbcParameter>();

        foreach (var (clause, value) in filters)
        {
            if (value == null)
                continue;
            sql.Append("\n   and ").Append(clause);
            parameters.Add(new OdbcParameter($"p{parameters.Count}", value));
        }

        if (tail.Length > 0)
            sql.Append('\n').Append(tail);

        return db.ExecuteQuery(sql.ToString(), maxRows, parameters.ToArray());
    }
}
