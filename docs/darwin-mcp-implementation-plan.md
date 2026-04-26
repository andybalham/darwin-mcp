# National Rail Darwin MCP Server — Implementation Plan

> **Learning focus:** This project is structured to build understanding progressively. Each phase
> introduces one or two new concepts before layering the next. Resist the urge to skip ahead —
> the early phases deliberately use simplified code so the MCP mechanics are visible before
> the real-world complexity of SOAP and async error handling enters the picture.

---

## Background & Goals

The Darwin OpenLDBWS API is a SOAP-based National Rail service that provides live departure
boards, service details, and disruption information. Wrapping it as an MCP server will give
Claude Code (and any other MCP client) the ability to answer questions like:

- *"What are the next trains from St Pancras to Luton Airport Parkway?"*
- *"Is the 08:42 from Bedford running on time?"*
- *"Are there any disruptions on the Thameslink route today?"*

The project is a natural companion to your commute dashboard work, and covers ground —
SOAP deserialization, structured tool design, async .NET patterns — that transfers directly
to other integrations.

---

## Prerequisites

| Requirement | Detail |
|---|---|
| Darwin API access | Register at [National Rail Open Data](https://opendata.nationalrail.co.uk/) for a free LDB token |
| .NET 9 SDK | Already in your environment |
| `ModelContextProtocol` NuGet | Microsoft's official .NET MCP server library |
| MCP client | Claude Code or Claude Desktop to test against |
| Optional | Postman or SoapUI for exploring the raw Darwin WSDL before coding |

---

## Phase 1 — Understand MCP Server Fundamentals

**Goal:** Get a minimal MCP server running and callable from Claude Code, with no external
dependencies at all. This phase is about the *framework*, not the domain.

### What you'll build

A hardcoded "echo" MCP server with one tool: `echo_message`. It takes a string and returns it.
Deliberately trivial — the point is observing the full MCP lifecycle.

### Key concepts introduced

- What an MCP server actually is (a process that speaks JSON-RPC over stdio or SSE)
- The `McpServerTool` attribute and how tools are discovered
- How tool parameters become JSON Schema automatically
- Registering the server in your Claude Code `mcp_servers` config

### Steps

1. Create a new console app: `dotnet new console -n DarwinMcp`
2. Add the package: `dotnet add package ModelContextProtocol`
3. Implement a single `[McpServerTool]` method that echoes its input
4. Wire up `McpServer.RunAsync()` in `Program.cs`
5. Add the server to Claude Code's MCP config (stdio transport)
6. Invoke `echo_message` from Claude Code and observe the raw JSON-RPC exchange in the logs

### Learning checkpoint

Before moving on, you should be able to explain: what happens between Claude Code sending a
`tools/call` request and your C# method returning a result. Look at the MCP spec's
`tools/call` section and map it to what you see in the logs.

---

## Phase 2 — Model the Darwin Domain

**Goal:** Design the data structures and tool signatures *before* writing any API client code.
Good MCP tool design is a skill in itself — parameters that are too granular make tools
hard for an LLM to use correctly; too broad and responses become unmanageably large.

### What you'll build

The domain model: record types for `DepartureBoard`, `ServiceSummary`, `CallingPoint`, etc.,
plus stub tool methods that return hardcoded sample data.

### Key concepts introduced

- Thinking about MCP tools from the LLM's perspective (what does Claude actually need to know?)
- The difference between a tool that returns raw API XML and one that returns a clean DTO
- How `[Description]` attributes on tools and parameters become the schema Claude reads

### Darwin tools to design

| Tool name | Parameters | Returns |
|---|---|---|
| `get_departures` | `fromCrs`, `toCrs?`, `numRows?` | List of departing services with platform, operator, delay status |
| `get_service_details` | `serviceId` | Calling points, times, delay reasons |
| `check_disruptions` | `crs` | Active NRCC messages for a station |
| `lookup_station` | `query` | CRS code lookup by station name fragment |

### Steps

1. Create a `Models/` folder and define the record types
2. Create a `Tools/` folder and stub each tool with `[McpServerTool]` and `[Description]` attributes
3. Return hardcoded sample data that looks realistic
4. Test all four tools from Claude Code and refine the descriptions until Claude uses them correctly

### Learning checkpoint

Ask Claude Code to answer: *"What time is the next train from Bedford to St Pancras?"* using
only your stub tools. If Claude struggles to pick the right tool or interpret the response,
adjust descriptions and structure before touching the real API.

---

## Phase 3 — Explore the Darwin SOAP API

**Goal:** Understand the Darwin WSDL and deserialize real responses before integrating
with your MCP tools. Isolate the API complexity in a separate layer.

### What you'll build

A standalone `DarwinClient` class (no MCP involvement yet) that calls the real API and
prints results to the console.

### Key concepts introduced

- How .NET handles SOAP via `HttpClient` + `XDocument` (preferred over adding a WCF service
  reference, which generates verbose scaffolding)
- SOAP envelope structure: how to construct a request and navigate the response namespace
- Dealing with Darwin-specific quirks: the `ldb` namespace, the token header, the
  `GetDepartureBoardRequest` shape

### Steps

1. Add a `Darwin/` folder with a `DarwinClient.cs`
2. Implement `GetDepartureBoardAsync(string fromCrs, string? toCrs, int numRows)` using
   raw `HttpClient` POST with a hand-crafted SOAP envelope string
3. Parse the XML response with `XDocument` and `XNamespace` — deliberately avoid auto-generated
   proxies at first so you can see what's actually being sent and received
4. Write a `Program.cs` scratch harness (separate from MCP wiring) that calls it and dumps results
5. Once working, optionally compare against a WCF-generated proxy to understand the trade-offs

### Sample SOAP envelope structure to understand

```xml
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"
               xmlns:typ="http://thalesgroup.com/RTTI/2013-11-28/Token/types"
               xmlns:ldb="http://thalesgroup.com/RTTI/2021-11-01/ldb/">
  <soap:Header>
    <typ:AccessToken>
      <typ:TokenValue>YOUR_TOKEN_HERE</typ:TokenValue>
    </typ:AccessToken>
  </soap:Header>
  <soap:Body>
    <ldb:GetDepartureBoardRequest>
      <ldb:numRows>10</ldb:numRows>
      <ldb:crs>BDM</ldb:crs>
      <ldb:filterCrs>STP</ldb:filterCrs>
    </ldb:GetDepartureBoardRequest>
  </soap:Body>
</soap:Envelope>
```

### Learning checkpoint

Print the raw XML response to console for a real station pair. Identify where the NRCC
messages live versus the individual service elements. This mental map of the XML structure
will make the deserialization code much easier to write correctly.

---

## Phase 4 — Connect the Real API to Your MCP Tools

**Goal:** Replace the stub data in your MCP tools with real Darwin responses. Handle errors
gracefully — the LLM will see whatever you return, including your error messages.

### Key concepts introduced

- The `IServiceProvider` / DI pattern in MCP server setup (injecting `DarwinClient` into tools)
- What MCP tools should return when an API call fails (a useful natural-language error, not
  an unhandled exception)
- Keeping tools thin: tools should translate, not process — mapping DTOs is the tool's job,
  business logic belongs in the client or a service layer

### Steps

1. Register `DarwinClient` in the DI container in `Program.cs`
2. Inject it into your tool class via constructor injection
3. Replace stub returns with real `DarwinClient` calls, mapping the XML response to your DTOs
4. Wrap calls in try/catch and return structured error strings that Claude can relay helpfully
   (e.g. `"Station code 'XYZ' not recognised by Darwin. Try lookup_station to find the correct CRS."`)
5. Implement `lookup_station` using a bundled CRS lookup dictionary (the NR station list
   is available as a static CSV — no API call needed)
6. Test the full chain end-to-end through Claude Code

### Error handling pattern to adopt

```csharp
[McpServerTool, Description("Get live departures from a station")]
public async Task<string> GetDepartures(string fromCrs, string? toCrs = null, int numRows = 10)
{
    try
    {
        var board = await _darwin.GetDepartureBoardAsync(fromCrs, toCrs, numRows);
        return JsonSerializer.Serialize(board); // or a formatted string
    }
    catch (DarwinApiException ex)
    {
        return $"Darwin API error: {ex.Message}. Check the CRS code is valid.";
    }
    catch (HttpRequestException)
    {
        return "Could not reach the Darwin API. Check your network connection.";
    }
}
```

---

## Phase 5 — Configuration, Secrets & Polish

**Goal:** Make the server production-ready for daily personal use. Establish patterns for
secrets management and configuration that you'll reuse in other MCP servers.

### Key concepts introduced

- Using `IConfiguration` and `appsettings.json` (with user secrets for the Darwin token)
- Structured logging in a stdio MCP server (you cannot log to stdout — use stderr or a file)
- Adding a `CLAUDE.md` so Claude Code understands the server's capabilities and limitations

### Steps

1. Move the Darwin token to `dotnet user-secrets` (never commit it):

   ```
   dotnet user-secrets set "Darwin:Token" "your-token-here"
   ```

2. Read config via `IConfiguration` injected at startup
3. Add `Microsoft.Extensions.Logging` with a file sink (e.g. `Serilog`) — stdio must stay
   clean for MCP JSON-RPC traffic
4. Create `CLAUDE.md` in the project root documenting:
   - Available tools and their typical use cases
   - CRS code format (three-letter uppercase, e.g. `BDM`, `STP`, `LTN`)
   - Known Darwin limitations (no historical data, live data only, 15-min token refresh)
5. Add `README.md` with setup instructions and MCP config snippet

### MCP config snippet for Claude Code

```json
{
  "mcpServers": {
    "darwin": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/dev/DarwinMcp"],
      "env": {}
    }
  }
}
```

---

## Phase 6 — Stretch Goals (Optional)

Once the core server is solid, these extensions deepen specific skills:

| Stretch goal | What you'll learn |
|---|---|
| **Add a `get_arrivals` tool** | Darwin has a separate `GetArrivalBoard` endpoint — follow the same pattern to add it |
| **Caching with `IMemoryCache`** | Darwin has rate limits; cache departure boards for 60s to avoid hammering the API during iterative Claude conversations |
| **SSE transport instead of stdio** | Swap from stdio to HTTP SSE so the server can run as a background service and be shared across machines on your local network |
| **Integration with your commute dashboard** | Call the same `DarwinClient` from your Lambda container — the client code is already environment-agnostic |
| **Natural language station lookup** | Pass ambiguous station names to a local LLM (Foundry Local / Phi) to resolve to CRS before calling Darwin — a small agentic pipeline within an MCP tool |

---

## Project Structure (End State)

```
DarwinMcp/
├── CLAUDE.md                   ← Tool guide for Claude Code
├── README.md
├── DarwinMcp.csproj
├── Program.cs                  ← Server setup, DI wiring, RunAsync
├── appsettings.json
├── Darwin/
│   ├── DarwinClient.cs         ← SOAP HTTP client
│   ├── DarwinApiException.cs
│   └── SoapEnvelopes.cs        ← Request envelope builders
├── Models/
│   ├── DepartureBoard.cs
│   ├── ServiceSummary.cs
│   ├── CallingPoint.cs
│   └── NrccMessage.cs
├── Tools/
│   ├── DeparturesTool.cs
│   ├── ServiceDetailsTool.cs
│   ├── DisruptionsTool.cs
│   └── StationLookupTool.cs
└── Data/
    └── stations.csv            ← Static CRS lookup table
```

---

## Key NuGet Packages

| Package | Purpose |
|---|---|
| `ModelContextProtocol` | MCP server host and tool registration |
| `Microsoft.Extensions.Hosting` | DI, configuration, logging infrastructure |
| `Serilog.Extensions.Logging` + sink | File logging (keep stdout clean for MCP) |
| `System.Xml.Linq` | `XDocument` / `XNamespace` for SOAP response parsing |

No SOAP framework (WCF, CoreWCF) is needed — raw `HttpClient` + `XDocument` keeps
dependencies minimal and makes the SOAP mechanics visible, which is the point at this
learning stage.

---

## Suggested Learning Journal Entries

As you work through each phase, log these in DevLog:

- **Phase 1:** What is the MCP stdio transport? What does a raw `tools/call` JSON-RPC message look like?
- **Phase 2:** What makes a good MCP tool description? What did you have to change to get Claude to use the right tool?
- **Phase 3:** How does SOAP differ from REST in terms of error signalling and namespace handling?
- **Phase 4:** What is the right level of granularity for MCP tool responses — raw data or pre-formatted text?
- **Phase 5:** Why can't an MCP stdio server log to stdout? What does "clean stdio" mean in this context?
