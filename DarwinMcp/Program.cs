using DarwinMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "darwin-mcp",
            Version = "0.1.0"
        };
    })
    .WithStdioServerTransport()
    .WithTools<EchoTool>();

await builder.Build().RunAsync();
