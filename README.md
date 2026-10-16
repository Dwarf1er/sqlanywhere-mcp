<div align="center">

# sqlanywhere-mcp
##### MCP server for SAP SQL Anywhere 17

<img alt="sqlanywhere-mcp logo" height="192" src="https://raw.githubusercontent.com/Dwarf1er/sqlanywhere-mcp/master/assets/logo.png" />

[![CI](https://img.shields.io/github/actions/workflow/status/Dwarf1er/sqlanywhere-mcp/ci.yml?style=for-the-badge&label=CI)](https://github.com/Dwarf1er/sqlanywhere-mcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/SqlAnywhereMcp?style=for-the-badge)](https://www.nuget.org/packages/SqlAnywhereMcp)
[![Release](https://img.shields.io/github/v/release/Dwarf1er/sqlanywhere-mcp?style=for-the-badge)](https://github.com/Dwarf1er/sqlanywhere-mcp/releases)
[![License](https://img.shields.io/github/license/Dwarf1er/sqlanywhere-mcp?style=for-the-badge)](https://github.com/Dwarf1er/sqlanywhere-mcp/blob/master/LICENSE)

</div>

<!-- mcp-name: io.github.Dwarf1er/sqlanywhere-mcp -->

MCP (*Model Context Protocol*) server for SQL Anywhere 17. Lets an MCP client such as Claude Code or Claude Desktop query a SQL Anywhere database directly to explore the schema, stored procedures, triggers, and views. It runs on Windows, Linux, and macOS.

> **Disclaimer:** sqlanywhere-mcp is an independent, community open-source project. It is not affiliated with, endorsed by, or sponsored by SAP SE. SAP and SQL Anywhere are trademarks or registered trademarks of SAP SE. sqlanywhere-mcp does not include or redistribute any SAP software.

## Table of contents

- [sqlanywhere-mcp](#sqlanywhere-mcp)
  - [Table of contents](#table-of-contents)
  - [Prerequisites](#prerequisites)
    - [ODBC driver setup](#odbc-driver-setup)
  - [Installation](#installation)
    - [From NuGet](#from-nuget)
    - [From a release](#from-a-release)
    - [From source](#from-source)
  - [Configuration](#configuration)
    - [File location](#file-location)
    - [Fields](#fields)
    - [Live reload](#live-reload)
  - [Database password](#database-password)
    - [Suggested helper: keyturn](#suggested-helper-keyturn)
  - [Claude Code integration](#claude-code-integration)
    - [As a plugin](#as-a-plugin)
    - [By hand](#by-hand)
  - [Claude Desktop integration](#claude-desktop-integration)
  - [Multiple databases](#multiple-databases)
  - [Available tools](#available-tools)
    - [Databases](#databases)
    - [Query execution](#query-execution)
    - [Ownership](#ownership)
    - [Schema introspection](#schema-introspection)
    - [Object lists](#object-lists)
    - [Source retrieval](#source-retrieval)
    - [Procedure calls](#procedure-calls)
  - [How it works](#how-it-works)
  - [License](#license)

## Prerequisites

- The **SQL Anywhere 17 ODBC driver**, installed on the machine that runs sqlanywhere-mcp. It comes with the [SQL Anywhere 17](https://www.sap.com/products/technology-platform/sybase-sql-anywhere.html) client or server install, for the platform you run on.
- On **Linux and macOS**, an ODBC driver manager: [unixODBC](https://www.unixodbc.org/) (for example `apt install unixodbc`, `dnf install unixODBC`, `brew install unixodbc`). On Windows the driver manager is built in.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), to build from source.
- An MCP client, such as [Claude Code](https://claude.ai/code) or Claude Desktop.

sqlanywhere-mcp itself is a .NET application with no platform-specific code. What differs per platform is how the driver is registered, which is covered in [ODBC driver setup](#odbc-driver-setup).

### ODBC driver setup

sqlanywhere-mcp connects with the ODBC driver named `SQL Anywhere 17`, which is the name the Windows installer registers. Use the `driver` setting (see [Fields](#fields)) when your driver is registered under another name.

- **Windows:** nothing to do after installing SQL Anywhere 17.
- **Linux and macOS:** unixODBC needs to know the driver. Either register it in `odbcinst.ini` under the name `SQL Anywhere 17`, or set `driver` in `config.json` to the path of the driver library, for example `/opt/sqlanywhere17/lib64/libdbodbc17.so` (the path depends on where SQL Anywhere is installed). The driver also needs its own libraries to be loadable; SQL Anywhere provides an `sa_config.sh` script for that, which you can source before starting your MCP client. See SAP's documentation for the details of your platform.

When the connection fails, the tool error names the driver it used, so a wrong driver name or a missing driver is visible in the client.

## Installation

There are three ways to get sqlanywhere-mcp. All of them give the same server, which needs the [prerequisites](#prerequisites) above (the ODBC driver, plus unixODBC on Linux and macOS).

### From NuGet

The [`SqlAnywhereMcp`](https://www.nuget.org/packages/SqlAnywhereMcp) package is a .NET tool with a self-contained build for each platform (`win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`), so no .NET runtime is needed to run it. It needs the .NET 10 SDK to install, which provides `dnx` and `dotnet tool`.

Run it without installing, which is what MCP clients usually do:

```bash
dnx SqlAnywhereMcp --yes
```

Or install it as a global tool, which puts `sqlanywhere-mcp` on your `PATH`:

```bash
dotnet tool install --global SqlAnywhereMcp
```

To register it with Claude Code without installing anything first:

```bash
claude mcp add --transport stdio --scope user sqlanywhere -- dnx SqlAnywhereMcp --yes
```

### From a release

Each [GitHub release](https://github.com/Dwarf1er/sqlanywhere-mcp/releases) has an archive per platform with the self-contained executable, plus a `SHA256SUMS` file. Unpack the one for your platform somewhere on your `PATH` and verify it with `sha256sum -c SHA256SUMS`. The executables are not code-signed, so macOS Gatekeeper and Windows SmartScreen may warn on first run.

### From source

Run the install script for your platform from a regular user account (no administrator rights needed). Each one publishes a self-contained executable, so the .NET runtime is not needed to run it.

**Windows** (PowerShell):

```powershell
.\install.ps1
```

It publishes `sqlanywhere-mcp.exe` to `%LOCALAPPDATA%\sqlanywhere-mcp\`, adds that directory to the user `PATH`, and prints the command to register the server with Claude Code.

| Option | Description |
|--------|-------------|
| `-Configuration Release` | Build configuration (default: `Release`) |
| `-Configuration Debug` | Build in Debug mode |
| `-Help` | Show help |

**Linux and macOS** (bash):

```bash
./install.sh
```

It detects the platform (`linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`), publishes `sqlanywhere-mcp` to `~/.local/bin/`, and prints the command to register the server with Claude Code. `-c Debug` builds in Debug mode.

The driver must match the architecture of the executable. If your SQL Anywhere 17 driver is x64 only, use an x64 build (on Apple silicon, the x64 build runs under Rosetta).

**Any platform**, by hand:

```bash
dotnet publish src/SqlAnywhereMcp -c Release -r <rid> --self-contained -o <output-dir>
```

where `<rid>` is a [.NET runtime identifier](https://learn.microsoft.com/dotnet/core/rid-catalog) such as `win-x64`, `linux-x64`, or `osx-x64`.

## Configuration

### File location

| Platform | Path |
|----------|------|
| Windows | `%APPDATA%\sqlanywhere-mcp\config.json` |
| Linux / macOS | `$XDG_CONFIG_HOME/sqlanywhere-mcp/config.json`, or `~/.config/sqlanywhere-mcp/config.json` when `XDG_CONFIG_HOME` is not set |

If the file is missing on first launch, an example is created automatically. Edit it and restart sqlanywhere-mcp once; after that, changes are picked up without a restart (see [Live reload](#live-reload)).

**Example configuration:**

```json
{
  "defaults": {
    "username": "dba",
    "passwordCommand": "keyturn get dev-db --refresh"
  },
  "allowedProcedures": [],
  "defaultDatabase": "demo",
  "databases": {
    "demo": { "host": "localhost", "database": "demodb" },
    "staging": {
      "host": "db.example.com",
      "port": 2700,
      "database": "stagingdb",
      "passwordCommand": "keyturn get staging-db --refresh",
      "allowedProcedures": ["dbo.sa_conn_info"]
    }
  }
}
```

Unknown fields are rejected, so a misspelled field (for example `allowedProcedure`) is reported instead of being silently ignored.

### Fields

Top level:

| Field | Required | Description |
|-------|----------|-------------|
| `databases` | Yes | The databases tools can reach, keyed by name. The name is what tools take as their `database` argument, and must be unique ignoring case. An empty object is valid: tools then report that no database is configured. See the fields below |
| `defaults` | No | Values every database uses unless it sets its own. See the fields below |
| `defaultDatabase` | No | Name of the database a tool uses when called without `database`. Not needed with a single database |
| `allowedProcedures` | No | Stored procedures that `call_procedure` may call on every database, each written as `owner.procedure` (default: none). See [Procedure calls](#procedure-calls) |

`defaults`:

| Field | Default | Description |
|-------|---------|-------------|
| `username` | - | Login username. Required, here or on each database |
| `passwordCommand` | - | Credential helper: a command that prints the database password. Required, here or on each database. See [Database password](#database-password) |
| `appInfo` | - | Value of the `AppInfo` ODBC parameter, used by SQL Anywhere to identify the connecting application (for example `my-app`) |
| `driver` | `SQL Anywhere 17` | ODBC driver name as registered with the driver manager, or the path of the driver library. See [ODBC driver setup](#odbc-driver-setup) |
| `port` | `2638` | TCP port of the database (SQL Anywhere's standard port) |

Each database:

| Field | Required | Description |
|-------|----------|-------------|
| `host` | Yes | Hostname of the SQL Anywhere server |
| `database` | Yes | Database name (`DBN` ODBC parameter) |
| `port` | No | Overrides `defaults.port` |
| `username` | Yes, unless in `defaults` | Overrides `defaults.username` |
| `passwordCommand` | Yes, unless in `defaults` | Overrides `defaults.passwordCommand`, for a database with other credentials |
| `appInfo` | No | Overrides `defaults.appInfo` |
| `driver` | No | Overrides `defaults.driver` |
| `allowedProcedures` | No | Procedures `call_procedure` may call on this database, in addition to the top-level ones |

### Live reload

sqlanywhere-mcp checks `config.json` before every tool call. When the file changed and is valid, the next call uses it: added databases become available, removed ones disappear, and open connections are closed so they reopen with the new settings. No restart of sqlanywhere-mcp or Claude Code is needed.

When the changed file is invalid (malformed JSON, an unknown field, a duplicate name, a bad procedure name, ...) or was deleted, sqlanywhere-mcp keeps using the last valid version. `list_databases` reports the error in `reloadError`, and the error is also written to sqlanywhere-mcp's standard error, which MCP clients show in their logs. Fixing the file clears it.

The first load at startup is still strict: sqlanywhere-mcp exits with an error when the file is invalid then.

## Database password

sqlanywhere-mcp never stores the database password. It follows the *credential-helper pattern*: `passwordCommand` is a command that prints the password on its standard output, and sqlanywhere-mcp runs it each time it opens a connection, so a password that rotates is picked up on the next reconnect.

The contract for a helper:

- It prints only the password on stdout. A trailing newline is ignored.
- It exits `0` on success. Any other exit code fails the connection, and the tool returns an error naming the command and its exit code.
- Diagnostics go to stderr, which sqlanywhere-mcp passes through to its own, where MCP clients show it in their logs.
- It runs without a shell and without input (stdin is closed). Quote arguments that contain spaces.

Any helper that follows this works, for example `pass show sqlanywhere/dev`, `secret-tool lookup service sqlanywhere`, or a small script of your own.

### Suggested helper: keyturn

[keyturn](https://github.com/Dwarf1er/keyturn) keeps a rotating secret in the OS credential store (Windows Credential Manager, macOS Keychain, Linux Secret Service) and asks for the new value when it expires. It is built around this pattern: `keyturn get NAME` prints the secret, and `--refresh` opens a prompt when the secret is stale or missing, then continues with the new value. One-time setup:

```bash
keyturn add dev-db daily@06:00 "SQL Anywhere dev password"
keyturn set dev-db
```

```json
"passwordCommand": "keyturn get dev-db --refresh"
```

[keyturn](https://github.com/Dwarf1er/keyturn) runs on Windows, Linux, and macOS. The command runs on the same operating system as sqlanywhere-mcp, so install keyturn there and make sure it is on that system's `PATH`. For example, when Claude Code runs inside WSL2 but launches the Windows `sqlanywhere-mcp.exe`, the helper is the Windows one and reads the Windows Credential Manager.

A database with other credentials can point at another secret with its own `passwordCommand`, overriding `defaults.passwordCommand`.

> `config.json` never holds the password: a config with a `password` field is rejected with an error.

## Claude Code integration

### As a plugin

The repository is also a Claude Code plugin marketplace. The plugin registers the server for you, and runs it with `dnx SqlAnywhereMcp --yes`, so it needs the .NET 10 SDK and always uses the latest published package. It doesn't create your `config.json`: see [Configuration](#configuration).

```bash
claude plugin marketplace add Dwarf1er/sqlanywhere-mcp
claude plugin install sqlanywhere@sqlanywhere-mcp
```

### By hand

The install script prints the exact command at the end of its run. The `--scope user` flag registers the server globally across all projects.

**Linux and macOS** - run from any terminal:
```bash
claude mcp add --transport stdio --scope user sqlanywhere \
  ~/.local/bin/sqlanywhere-mcp
```

**Windows** - run from any terminal:
```powershell
claude mcp add --transport stdio --scope user sqlanywhere `
  "C:\Users\<username>\AppData\Local\sqlanywhere-mcp\sqlanywhere-mcp.exe"
```

**Claude Code inside WSL2** - there are two options:

- Install and run it as a Linux program inside WSL2, as on Linux above. This needs the Linux SQL Anywhere 17 driver and unixODBC inside WSL2.
- Launch the Windows executable through WSL2 interop, which uses the Windows driver. stdio communication works transparently:
  ```bash
  claude mcp add --transport stdio --scope user sqlanywhere \
    /mnt/c/Users/<username>/AppData/Local/sqlanywhere-mcp/sqlanywhere-mcp.exe
  ```

Restart Claude Code for the configuration to take effect. The MCP server starts automatically when Claude Code launches.

## Claude Desktop integration

Claude Desktop reads its MCP server list from `claude_desktop_config.json`. Open it via Settings > Developer > Edit Config (this creates the file if it doesn't exist yet), and add an entry under `mcpServers`:

```json
{
  "mcpServers": {
    "sqlanywhere": {
      "command": "C:\\Users\\<username>\\AppData\\Local\\sqlanywhere-mcp\\sqlanywhere-mcp.exe"
    }
  }
}
```

On macOS, use the path of the installed executable instead, for example `/Users/<username>/.local/bin/sqlanywhere-mcp`.

If the file already has other entries under `mcpServers`, just add `sqlanywhere` alongside them.

Save the file and fully restart Claude Desktop (quit it completely, not just close the window). To verify the connection, click the "+" button in the chat box and select "Connectors" - `sqlanywhere` should appear with its tools listed. If it fails to connect, check Settings > Developer for logs.

## Multiple databases

List every database under `databases` in one `config.json`, keyed by name (see [Configuration](#configuration)). Every tool takes an optional `database` argument with the name of the database to use; without it, the tool uses `defaultDatabase`, or the only database when there is just one. `list_databases` lists the names that exist.

Registering sqlanywhere-mcp several times, each with its own config file through the `SQLANYWHERE_MCP_CONFIG` environment variable, still works. It is only useful to keep databases fully apart, for example a separate server entry for production:

```bash
claude mcp add --transport stdio --scope user sqlanywhere-prod \
  --env SQLANYWHERE_MCP_CONFIG=~/.config/sqlanywhere-mcp/prod.json \
  -- ~/.local/bin/sqlanywhere-mcp
```

When `SQLANYWHERE_MCP_CONFIG` is not set, the server uses the default config path.

## Available tools

Tool names are in `snake_case`, as clients see them (for example `list_databases`). Every tool except `call_procedure` is annotated as read-only, idempotent and closed-world (`readOnlyHint`, `idempotentHint`, `openWorldHint`), so MCP clients can approve them without prompting; `call_procedure` is annotated as non-read-only and destructive. Failures are returned as MCP tool errors (`isError`), with the reason as the message.

Every tool declares an `outputSchema` and returns its result as `structuredContent` (the same JSON is also in the text content for clients that ignore structured output). Most tools return `{ "rows": [...], "truncated": bool, "count": int }`, where each row is an object keyed by column name. `explain_query` returns `{ "query", "plan", "estimatedRows", "estimatedTime" }` and `list_databases` returns the databases and the config status.

Tool calls are cancellable: when the client cancels a request, sqlanywhere-mcp asks the database to stop the running statement and stops a password command that is still running.

Every tool below except `list_databases` also takes an optional `database` parameter: the name of the database to use, from `list_databases`. Without it, the tool uses the default database. The parameter is left out of the tables below.

### Databases

#### `list_databases`

Lists the configured databases with their name, host, port, database name, username, and the procedures `call_procedure` may call on each, along with the default database, the config file path, and `reloadError` when the last change to `config.json` could not be loaded.

### Query execution

#### `execute_query`

Executes a read-only SQL query and returns results as JSON. Only `SELECT` and `WITH` statements are accepted. Multi-statement queries (`;`) and `SELECT ... INTO` are rejected. A query that runs longer than 60 seconds is cancelled.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `sql` | `string` | - | SQL query to execute |
| `maxRows` | `int` | `500` | Maximum number of rows returned (at most 10000) |

The response includes a `truncated` flag set to `true` when the result exceeds `maxRows`, and a `count` field with the number of rows returned.

#### `explain_query`

Returns the execution plan for a read-only SQL query (`SELECT` or `WITH` only).

| Parameter | Type | Description |
|-----------|------|-------------|
| `sql` | `string` | SQL query to explain |

#### `describe_query`

Describes the result set of a read-only query without running it (`sa_describe_query`): each column's name, data type, nullability, and the table column it comes from. The same read-only checks as `execute_query` apply.

| Parameter | Type | Description |
|-----------|------|-------------|
| `sql` | `string` | `SELECT` or `WITH` query to describe |

### Ownership

#### `list_owners`

Lists all distinct owners (users) that own at least one table or view in the database.

### Schema introspection

#### `list_tables`

Lists all base tables and views with their type and owner.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `owner` | `string` | - | Filter results to a specific owner |
| `nameFilter` | `string` | - | SQL `LIKE` pattern to search by name (e.g. `cust%`, `%order%`) |

#### `describe_table`

Returns the column definitions of a table: name, type, length, nullability, default value, primary key flag, and comment.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `tableName` | `string` | - | Table name |
| `owner` | `string` | - | Owner name to disambiguate when multiple owners share the same table name |

#### `list_check_constraints`

Lists all check constraints on a table with their name and definition.

| Parameter | Type | Description |
|-----------|------|-------------|
| `tableName` | `string` | Table name |

#### `list_indexes`

Lists all indexes of a table: primary key, foreign key, secondary, and text indexes, with their uniqueness and the columns they cover in order (descending columns are marked `desc`).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `tableName` | `string` | - | Table name |
| `owner` | `string` | - | Owner name to disambiguate when multiple owners share the same table name |

#### `list_foreign_keys`

Lists all foreign keys in the database: role name, child table, parent table, and mapped columns.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `tableName` | `string` | - | Filter to a specific child (referencing) table |

#### `get_table_definition`

Returns the `CREATE` statements the database generates for a table (`sa_get_table_definition`): the table and its columns, keys, foreign keys, the sequences it uses, and granted privileges.

Triggers and comments are left out by default: a table with a few large triggers can produce more output than an MCP client accepts in one call, since each trigger appears twice (its definition and its preserved original source). `get_trigger_source` and `get_table_comments` return them separately.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `tableName` | `string` | - | Table name |
| `owner` | `string` | - | Owner name to disambiguate when multiple owners share the same table name |
| `includeTriggers` | `bool` | `false` | Include the table's triggers |
| `includeComments` | `bool` | `false` | Include the `COMMENT ON` statements for the table and its columns |

#### `get_table_comments`

Returns the comment of a table and of each of its columns: one `table` row, then one row per column, each with the database's own comment (`remarks`, set with `COMMENT ON`).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `tableName` | `string` | - | Table name |
| `owner` | `string` | - | Owner name to disambiguate when multiple owners share the same table name |

#### `list_dependent_views`

Lists the views that depend on a table or view, directly or through other views (`sa_dependent_views`).

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `tableName` | `string` | - | Table or view name |
| `owner` | `string` | - | Owner name to disambiguate when multiple owners share the same table name |

#### `describe_procedure`

Describes a stored procedure or function: its parameters (`in`, `out`, `inout`) with their types and defaults, the columns of the result set it returns, and a function's return value.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `procName` | `string` | - | Procedure or function name |
| `owner` | `string` | - | Owner name to disambiguate when multiple owners share the same procedure name |

#### `get_table_stats`

Returns the exact row count for a table. Table and owner names must contain only letters, digits, and underscores.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `tableName` | `string` | - | Table name |
| `owner` | `string` | - | Owner name for schema-qualified access |

### Object lists

#### `list_procedures`

Lists all stored procedures and functions in the database, each tagged as `procedure` or `function`.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `objectType` | `string` | - | Filter to `procedure` or `function` |

#### `list_views`

Lists all views in the database.

#### `list_triggers`

Lists all triggers in the database with their associated table.

#### `get_database_info`

Returns database-level properties: name, product version, character set, collation, and page size.

#### `list_events`

Lists all database events (scheduled jobs) with their name and enabled status.

#### `list_sequences`

Lists all sequences with their owner, start value, increment, bounds, and cycling.

#### `list_user_types`

Lists all user-defined data types (domains) with their owner, base type, nullability, default, and check condition.

#### `list_web_services`

Lists the web services defined in the database with their type (HTTP, SOAP, DISH, RAW, ...), whether they're enabled, whether they require authentication or HTTPS, and the user they run as.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `nameFilter` | `string` | - | SQL `LIKE` pattern to search by name |

### Source retrieval

When the database's `preserve_source_format` option is On, SQL Anywhere keeps the original source of procedures, views, triggers, and events, with comments and formatting. The source tools return that original source, and fall back to the definition as reformatted by the server when there is none.

#### `get_procedure_source`

Returns the full source definition of a stored procedure or function.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `procName` | `string` | - | Stored procedure name |
| `owner` | `string` | - | Owner name to disambiguate when multiple owners share the same procedure name |

#### `get_view_source`

Returns the full source definition of a view.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `viewName` | `string` | - | View name |
| `owner` | `string` | - | Owner name to disambiguate when multiple owners share the same view name |

#### `get_trigger_source`

Returns the full source definition of a trigger.

| Parameter | Type | Description |
|-----------|------|-------------|
| `triggerName` | `string` | Trigger name |

#### `get_event_source`

Returns the full source definition of a database event.

| Parameter | Type | Description |
|-----------|------|-------------|
| `eventName` | `string` | Event name |

#### `get_web_service_source`

Returns the definition of a web service: the SQL statement it runs, its URL handling, the DISH prefix for DISH services, and its remarks.

| Parameter | Type | Description |
|-----------|------|-------------|
| `serviceName` | `string` | Web service name |

#### `search_source`

Searches the source of procedures and functions, views, triggers, events, and web service statements for a piece of text, case-insensitively, and lists the objects that contain it. The text is matched literally: `%` and `_` aren't wildcards.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` | `string` | - | Text to search for |
| `objectType` | `string` | - | Search one kind only: `procedure`, `view`, `trigger`, `event`, or `web service` |
| `maxRows` | `int` | `200` | Maximum number of objects returned |

### Procedure calls

#### `call_procedure`

Calls a stored procedure allowed for the database in `config.json`: the top-level `allowedProcedures` apply to every database, and a database's own `allowedProcedures` add to them. Every other procedure is rejected, and the error lists the allowed ones. With no allowed procedure, which is the default, every call is rejected.

```json
"allowedProcedures": ["dbo.sa_db_info"],
"databases": {
  "demo": { "host": "localhost", "database": "demodb", "allowedProcedures": ["dbo.sa_conn_info"] }
}
```

Here `demo` allows both procedures, and any other database only `dbo.sa_db_info`.

Only list procedures you are comfortable letting Claude run: they run with the connection's permissions, outside the read-only check of `execute_query`.

| Parameter | Type | Description |
|-----------|------|-------------|
| `procName` | `string` | Procedure name, or `owner.procedure` when two allowed procedures share a name |
| `args` | `object` | The procedure's named parameters and their values. Parameters left out use the procedure's defaults |

## How it works

The server keeps one persistent ODBC connection per database, opened on the first tool call that uses it, pinging before each operation and reconnecting automatically if the connection is dropped. A change to `config.json` closes the connections, which reopen with the new settings on the next call.

`execute_query` enforces read-only access at the application level: it strips comments, rejects anything that is not a `SELECT` or `WITH` statement, and blocks multi-statement queries and `SELECT ... INTO`. This is a safeguard, not a security boundary: for real protection, connect with a database user that only has read permissions. All other tools use hardcoded parameterized queries against SQL Anywhere's system catalog.

| System table | Contents |
|--------------|----------|
| `sys.systab` | Tables and views |
| `sys.systabcol` | Table columns |
| `sys.sysuser` | Users and owners |
| `sys.syscolumns` | Columns with types, widths, nullability, and comments |
| `sys.sysidx` | Index definitions |
| `sys.sysidxcol` | Index-to-column mappings |
| `sys.sysprocedure` | Stored procedures and functions with source code |
| `sys.sysprocparm` | Procedure parameters and result columns |
| `sys.systrigger` | Triggers with source code |
| `sys.systriggers` | Triggers with their table |
| `sys.sysview` | View definitions |
| `sys.sysviews` | Views |
| `sys.syssource` | Original source of views |
| `sys.sysforeignkeys` | Foreign key relationships |
| `sys.sysevent` | Database events (scheduled jobs) with source code |
| `sys.sysconstraint` | Constraint metadata (check, PK, FK, unique) |
| `sys.syscheck` | Check constraint definitions |
| `sys.sysremark` | Table and column comments |
| `sys.syssequence` | Sequences |
| `sys.sysusertype` | User-defined data types |
| `sys.syswebservice` | Web services |

They also call the `sa_get_table_definition`, `sa_dependent_views`, and `sa_describe_query` system procedures. None of these are deprecated compatibility views.

The MCP server runs in `stdio` mode and is managed automatically by Claude Code.

## License

This software is licensed under the [MIT license](https://github.com/Dwarf1er/sqlanywhere-mcp/blob/master/LICENSE).

SAP and SQL Anywhere are trademarks or registered trademarks of SAP SE. This project is not affiliated with or endorsed by SAP SE.
