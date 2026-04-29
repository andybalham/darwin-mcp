# Darwin MCP Server

National Rail Darwin (OpenLDBWS) wrapped as a Model Context Protocol server. Lets Claude Code (or any MCP client) query live UK train departures, service details, and disruptions.

Built with .NET 10 and the [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) NuGet package. Raw `HttpClient` + `XDocument` for SOAP — no WCF.

## Tools

| Tool | Purpose |
|---|---|
| `get_departures` | Live departures from a station, optional destination filter |
| `get_service_details` | Calling points, times, delay reasons for a specific service |
| `check_disruptions` | Active NRCC messages for a station |
| `lookup_station` | CRS code lookup by station name fragment |

CRS codes are three-letter uppercase station identifiers (e.g. `BDM` Bedford, `STP` St Pancras, `LTN` Luton Airport Parkway).

## Setup

1. Register for a free Darwin LDB token at [National Rail Open Data](https://opendata.nationalrail.co.uk/).

2. Store the token via user-secrets (never commit it):

   ```bash
   cd DarwinMcp
   dotnet user-secrets set "Darwin:Token" "your-token-here"
   ```

3. Build:

   ```bash
   dotnet build
   ```

## MCP client config

### Claude Code

`~/.claude.json` or workspace `.mcp.json`:

```json
{
  "mcpServers": {
    "darwin": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/darwin-mcp/DarwinMcp"],
      "env": {}
    }
  }
}
```

### Claude Desktop

Edit the Claude Desktop config file:

- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

Add the same `mcpServers` entry as above, then restart Claude Desktop. Verify under Settings → Developer → MCP servers; the hammer icon in a chat lists registered tools.

**Tips:**

- `dotnet run` rebuilds on every launch — slow. For daily use, publish once and point `command` at the binary directly:

  ```bash
  dotnet publish -c Release
  ```

  ```json
  "darwin": {
    "command": "C:/path/to/darwin-mcp/DarwinMcp/bin/Release/net10.0/publish/DarwinMcp.exe"
  }
  ```

- `dotnet user-secrets` only resolves when launched from the project directory. A published binary loses access — pass the token via env var instead:

  ```json
  "darwin": {
    "command": "C:/path/to/publish/DarwinMcp.exe",
    "env": { "DARWIN_TOKEN": "your-token" }
  }
  ```

- Logs land at `<binary-dir>/logs/darwin-YYYYMMDD.log` — first stop if the server fails to register.

## Logging

Stdio MCP reserves stdout for JSON-RPC. All logs go to a file sink (Serilog) configured in `appsettings.json`:

- Path: `logs/darwin-YYYYMMDD.log` next to the binary (`bin/Debug/net10.0/logs/`)
- Rolling: daily, 7-day retention

Adjust levels or sinks in `appsettings.json`. Never add a console sink — it would corrupt the JSON-RPC stream.

## Probe mode (no MCP)

Standalone harness for sanity-checking the Darwin client. Reads token from `DARWIN_TOKEN` env var.

```bash
dotnet run -- --probe BDM           # all departures from Bedford
dotnet run -- --probe BDM STP       # Bedford → St Pancras filter
```

Output goes to stderr.

## Limitations

- Live data only. No historical departures or arrivals.
- Darwin tokens are rate-limited.
- Token must be refreshed periodically per Darwin terms.
