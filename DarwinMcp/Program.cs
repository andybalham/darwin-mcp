using DarwinMcp.Darwin;
using DarwinMcp.Data;
using DarwinMcp.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Phase 3 scratch harness — kept separate from MCP wiring per the plan.
// Invocation:
//   dotnet run -- --probe BDM           (all departures from Bedford)
//   dotnet run -- --probe BDM STP       (Bedford → St Pancras filter)
// Token comes from env var DARWIN_TOKEN (Phase 5 will move to user-secrets).
// Writes to stderr — never stdout, so behaviour stays consistent with the
// MCP-mode "stdout is JSON-RPC only" rule even though MCP isn't running here.
if (args.Length >= 2 && args[0] == "--probe")
{
    await RunProbe(args);
    return;
}

// Generic .NET host — gives us DI, configuration, logging, and graceful shutdown.
// MCP server runs as a hosted service inside it.
var builder = Host.CreateApplicationBuilder(args);

// User-secrets only loaded by default in Development env; MCP launches as
// Production, so wire explicitly to pick up `dotnet user-secrets set Darwin:Token`.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// CRITICAL for stdio MCP: stdout is reserved exclusively for JSON-RPC traffic.
// Default host adds a console logger that writes to stdout — that would corrupt
// the protocol stream and the client would see garbled JSON. Clear all providers.
// (Phase 5 will re-add a Serilog file sink so we can still see logs.)
builder.Logging.ClearProviders();

// Phase 4 DI wiring.
//
// Token resolution (in order):
//   1. Configuration "Darwin:Token" — picks up appsettings.json + env
//      vars (DOTNET prefix strips to "Darwin__Token") + user-secrets when
//      Phase 5 enables them.
//   2. Bare DARWIN_TOKEN env var — kept for parity with the Phase 3 probe.
// If neither is set we let DarwinClient be constructed with an empty
// token; the first SOAP call will fail with a clear Darwin auth error
// rather than crashing at startup. That keeps `tools/list` working so the
// MCP client can still introspect the server before a token is provided.
var darwinToken = builder.Configuration["Darwin:Token"]
                  ?? Environment.GetEnvironmentVariable("DARWIN_TOKEN")
                  ?? string.Empty;

// Named HttpClient for DarwinClient. AddHttpClient gives us pooled
// connection reuse via IHttpClientFactory and avoids the classic
// "new HttpClient per call" socket-exhaustion trap.
builder.Services.AddHttpClient("darwin", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});
// DarwinClient is a singleton — it holds no per-request state and its
// HttpClient is sourced from the factory each call (factory itself is
// thread-safe and pools HttpMessageHandlers internally).
builder.Services.AddSingleton<DarwinClient>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("darwin");
    return new DarwinClient(http, darwinToken);
});

// Station lookup is stateless after construction — singleton avoids
// re-reading stations.csv on every tool call.
builder.Services.AddSingleton<StationLookup>();

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
    // Reflection-scans each tool class for [McpServerTool] methods and registers
    // them. Method signature → JSON Schema for the tool's input. PascalCase
    // method names auto-convert to snake_case (GetDepartures → get_departures).
    //
    // EchoTool stays for now as a sanity check — drop it in Phase 5 cleanup.
    // Phase 4: tool classes now depend on DarwinClient / StationLookup via
    // constructor injection (the SDK resolves them through IServiceProvider).
    .WithTools<EchoTool>()
    .WithTools<DeparturesTool>()
    .WithTools<ServiceDetailsTool>()
    .WithTools<DisruptionsTool>()
    .WithTools<StationLookupTool>();

// Blocks until stdin closes or Ctrl+C. Hosted service handles the protocol loop.
await builder.Build().RunAsync();
return;

// Local function so the probe path stays out of the MCP host's lifetime.
static async Task RunProbe(string[] args)
{
    var token = Environment.GetEnvironmentVariable("DARWIN_TOKEN");
    if (string.IsNullOrWhiteSpace(token))
    {
        await Console.Error.WriteLineAsync("DARWIN_TOKEN env var not set. Get a free token at https://opendata.nationalrail.co.uk/");
        Environment.ExitCode = 2;
        return;
    }

    var fromCrs = args[1].ToUpperInvariant();
    var toCrs = args.Length >= 3 ? args[2].ToUpperInvariant() : null;

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    var client = new DarwinClient(http, token);

    try
    {
        var board = await client.GetDepartureBoardAsync(fromCrs, toCrs, numRows: 10);
        await Console.Error.WriteLineAsync($"=== {board.StationName} ({board.Crs}) @ {board.GeneratedAt} ===");
        foreach (var s in board.Services)
        {
            await Console.Error.WriteLineAsync(
                $"{s.ScheduledDeparture}  {(s.EstimatedDeparture ?? "-"),-10}  plat {s.Platform ?? "?"}  " +
                $"{s.Operator,-20}  {s.Origin} → {s.Destination}" +
                (s.IsCancelled ? "  [CANCELLED]" : "") +
                (s.DelayReason is null ? "" : $"  ({s.DelayReason})"));
        }
        if (board.Messages.Count > 0)
        {
            await Console.Error.WriteLineAsync("--- NRCC ---");
            foreach (var m in board.Messages)
                await Console.Error.WriteLineAsync($"[sev {m.Severity}] {m.Message}");
        }
    }
    catch (DarwinApiException ex)
    {
        await Console.Error.WriteLineAsync($"Darwin error: {ex.Message}");
        Environment.ExitCode = 1;
    }
    catch (HttpRequestException ex)
    {
        await Console.Error.WriteLineAsync($"Network error: {ex.Message}");
        Environment.ExitCode = 1;
    }
}
