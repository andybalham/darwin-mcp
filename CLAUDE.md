# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

National Rail Darwin MCP Server — wraps the Darwin OpenLDBWS SOAP API as an MCP server so Claude Code can query live UK train departures, service details, and disruptions. Built with .NET 9 and the `ModelContextProtocol` NuGet package.

## Build & Run

```bash
# Build
dotnet build

# Run (stdio MCP server — stdout is reserved for JSON-RPC, never log there)
dotnet run

# Run tests
dotnet test

# Manage Darwin API token (never commit tokens)
dotnet user-secrets set "Darwin:Token" "your-token-here"
```

## Architecture

```
DarwinMcp/
├── Program.cs              ← Server setup, DI wiring, McpServer.RunAsync()
├── Darwin/
│   ├── DarwinClient.cs     ← Raw HttpClient SOAP calls + XDocument parsing
│   ├── DarwinApiException.cs
│   └── SoapEnvelopes.cs    ← SOAP envelope builders
├── Models/                 ← Record DTOs (DepartureBoard, ServiceSummary, CallingPoint, etc.)
├── Tools/                  ← [McpServerTool] methods — thin translation layer, no business logic
│   ├── DeparturesTool.cs
│   ├── ServiceDetailsTool.cs
│   ├── DisruptionsTool.cs
│   └── StationLookupTool.cs
└── Data/
    └── stations.csv        ← Static CRS code lookup (no API call needed)
```

### Key design decisions

- **No WCF/CoreWCF** — raw `HttpClient` + `XDocument` for SOAP. Keeps dependencies minimal and SOAP mechanics visible.
- **Tools are thin** — they map DTOs and handle errors, business logic lives in `DarwinClient`.
- **Stdio transport** — stdout is exclusively for MCP JSON-RPC traffic. All logging goes to stderr or file (Serilog).
- **DI via `IServiceProvider`** — `DarwinClient` registered in DI container, injected into tool classes via constructor.

## MCP Tools

| Tool | Purpose |
|---|---|
| `get_departures` | Live departures from a station, optional destination filter |
| `get_service_details` | Calling points, times, delay reasons for a specific service |
| `check_disruptions` | Active NRCC messages for a station |
| `lookup_station` | CRS code lookup by station name fragment |

## Domain Knowledge

- **CRS codes**: three-letter uppercase station identifiers (e.g. `BDM` = Bedford, `STP` = St Pancras, `LTN` = Luton Airport Parkway)
- **Darwin API**: SOAP-based, uses `ldb` namespace (`http://thalesgroup.com/RTTI/2021-11-01/ldb/`), token passed in SOAP header
- **Limitations**: live data only (no historical), rate-limited

## Key NuGet Packages

- `ModelContextProtocol` — MCP server host and tool registration
- `Microsoft.Extensions.Hosting` — DI, configuration, logging
- `Serilog.Extensions.Logging` — file logging (keep stdout clean)

## MCP Client Config

```json
{
  "mcpServers": {
    "darwin": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/DarwinMcp"]
    }
  }
}
```
