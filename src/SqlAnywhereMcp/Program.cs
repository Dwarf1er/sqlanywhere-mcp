using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlAnywhereMcp;
using SqlAnywhereMcp.Config;
using SqlAnywhereMcp.Database;
using SqlAnywhereMcp.Models;
using SqlAnywhereMcp.Tools;

var path = ConfigService.FilePath;
var loaded = ConfigService.Load(path);
if (loaded is Err<RootConfig> err)
{
    Console.Error.WriteLine(err.Message);
    return 1;
}
var config = ((Ok<RootConfig>)loaded).Value;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder
    .Services.AddSingleton(new DatabaseRegistry(path, config))
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "sqlanywhere", Version = ServerVersion() };
        options.ServerInstructions =
            "Read-only access to SAP SQL Anywhere databases configured in sqlanywhere-mcp's config.json. "
            + "Call list_databases first to see the available databases; every other tool takes an "
            + "optional 'database' argument. Use the schema tools (list_tables, describe_table, "
            + "get_procedure_source, ...) to explore, and execute_query for SELECT or WITH queries. "
            + "call_procedure only runs procedures the user allowlisted and may have side effects.";
    })
    .WithStdioServerTransport()
    .WithTools<DbTools>();

await builder.Build().RunAsync();
return 0;

// The package version, such as 1.2.3 or 1.2.3-rc.1, without the source revision the SDK appends after "+".
static string ServerVersion() =>
    typeof(DatabaseRegistry)
        .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion.Split('+')[0]
    ?? "0.0.0";
