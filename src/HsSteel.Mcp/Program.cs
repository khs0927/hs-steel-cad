using HsSteel.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// HS-Steel MCP server (stdio). After the power-cad merge, HsTools is registered in power-cad-server too.
if (args.Length > 0 && args[0] == "--demo")
{
    var output = args.Length > 1 ? args[1] : "out/demo.dxf";
    Console.WriteLine(Demo.Run(output));
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(Workspace.FromEnvironment());
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<HsTools>();
await builder.Build().RunAsync();
