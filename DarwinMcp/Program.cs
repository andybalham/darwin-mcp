using DarwinMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Generic .NET host — gives us DI, configuration, logging, and graceful shutdown.
// MCP server runs as a hosted service inside it.
var builder = Host.CreateApplicationBuilder(args);

// CRITICAL for stdio MCP: stdout is reserved exclusively for JSON-RPC traffic.
// Default host adds a console logger that writes to stdout — that would corrupt
// the protocol stream and the client would see garbled JSON. Clear all providers.
// (Phase 5 will re-add a Serilog file sink so we can still see logs.)
builder.Logging.ClearProviders();

builder.Services
    // Registers the MCP server, its request handlers, and a hosted service
    // that drives the read/write loop until stdin closes.
    .AddMcpServer(options =>
    {
        // Advertised in the `initialize` response — clients log this.
        options.ServerInfo = new()
        {
            Name = "darwin-mcp",
            Version = "0.1.0"
        };
    })
    // Stdio transport: server reads JSON-RPC requests from stdin, writes
    // responses to stdout, one message per line. Client (Claude Code) spawns
    // this process and pipes to it. Process exits when stdin closes.
    .WithStdioServerTransport()
    // Reflection-scans EchoTool for [McpServerTool] methods and registers each
    // as a callable tool. Method signature → JSON Schema for the tool's input.
    // PascalCase method names auto-convert to snake_case (EchoMessage → echo_message).
    .WithTools<EchoTool>();

// Blocks until stdin closes or Ctrl+C. Hosted service handles the protocol loop.
await builder.Build().RunAsync();
