# Darwin MCP — Implementation Status

Tracks progress against [darwin-mcp-implementation-plan.md](./darwin-mcp-implementation-plan.md).

| Phase | Status | Notes |
|---|---|---|
| 1 — MCP server fundamentals | ✅ Complete | Echo tool live, verified via Claude Code |
| 2 — Domain model + tool stubs | ✅ Complete | 4 stub tools + DTOs registered |
| 3 — Darwin SOAP client | ✅ Complete | `DarwinClient` + `--probe` harness; not yet wired into MCP tools |
| 4 — Wire real API to tools | ✅ Complete | All four tools call real Darwin via DI; `stations.csv` bundled |
| 5 — Config, secrets, polish | ✅ Complete | User-secrets token, Serilog file sink, EchoTool dropped, appsettings + README |
| 6 — Stretch goals | ⏳ Not started | |

---

## Phase 1 — Complete

**Built:**

- `DarwinMcp` console project (.NET 10), `ModelContextProtocol` 1.2.0 + `Microsoft.Extensions.Hosting` 10.0.7
- `Program.cs` — generic host, MCP server, stdio transport, logging cleared (stdout reserved for JSON-RPC)
- `Tools/EchoTool.cs` — single `[McpServerTool]` method `EchoMessage` → registered as `echo_message`
- `.mcp.json` at repo root — registers server with Claude Code (project-scoped)
- `.gitignore` (dotnet template)

**Verified:**

- `initialize` / `tools/list` / `tools/call` round-trip via piped stdin
- Live call from Claude Code: `echo_message("Pong")` → `"Echo: Pong"`

**Learning checkpoint hit:**

- Tool name auto-converts PascalCase → snake_case
- `[Description]` on method = tool description; on parameter = schema property description
- Stdio transport requires clean stdout — default console logger had to be cleared
- `[McpServerToolType]` on class is required for `WithTools<T>()` reflection scan

**Open questions for next phase:**

- How granular should Darwin tools be? (one big `get_departure_board` vs split by use case)
- Where to draw line between client-layer DTO mapping and tool-layer formatting

---

## Phase 2 — Complete

**Built:**

- `Models/` — `DepartureBoard`, `ServiceSummary`, `CallingPoint`, `ServiceDetails`, `NrccMessage`, `StationMatch` (all `record` types, immutable DTOs)
- `Tools/DeparturesTool.cs` → `get_departures(fromCrs, toCrs?, numRows?)` — returns `DepartureBoard` stub
- `Tools/ServiceDetailsTool.cs` → `get_service_details(serviceId)` — returns `ServiceDetails` stub with calling points
- `Tools/DisruptionsTool.cs` → `check_disruptions(crs)` — returns `NrccMessage[]` stub
- `Tools/StationLookupTool.cs` → `lookup_station(query)` — in-memory sample with case-insensitive `Contains` match
- `Program.cs` — all four tool classes registered via `WithTools<T>()` alongside `EchoTool`

**Design decisions:**

- Times stay as raw strings ("On time", "08:55", HH:mm) — Darwin returns these mixed forms; LLM consumes them better than parsed `DateTime`
- `ServiceId` surfaced as opaque token (Darwin RID-style); not human-meaningful but required for `get_service_details`
- NRCC messages embedded in `DepartureBoard` AND exposed via `check_disruptions` — dedicated tool avoids pulling full board for disruption-only queries
- Tool descriptions explicitly mention CRS format + cross-reference (`lookup_station` first if name-only) so LLM picks correct sequence

**Verified:**

- Compile clean (output copy failed only because running MCP server held `DarwinMcp.exe` lock — code itself compiled)
- Pending: live Claude Code call to confirm `tools/list` shows 5 tools and stub responses round-trip

**Open questions for next phase:**

- Whether to keep `Severity` as string in `NrccMessage` or map to enum once Darwin's exact values seen in Phase 3
- Should `lookup_station` rank matches (prefix > contains) when CSV is wired in Phase 4

---

## Phase 3 — Complete

**Built:**

- `Darwin/DarwinApiException.cs` — single typed exception for SOAP fault, HTTP non-success, and parse failure paths
- `Darwin/SoapEnvelopes.cs` — hand-built envelope strings, `SecurityElement.Escape` on every interpolated value, optional `filterCrs` element
- `Darwin/DarwinClient.cs` — `HttpClient` POST against `lite.realtime.nationalrail.co.uk/OpenLDBWS/ldb12.asmx`, parses response with `XDocument` + local-name navigation
- `Program.cs` — `--probe <fromCrs> [toCrs]` mode: skips MCP host, dumps parsed `DepartureBoard` to **stderr** (stdout-clean rule still honoured), reads token from `DARWIN_TOKEN` env var

**Design decisions:**

- No WCF / svcutil proxy — every byte on the wire stays visible (per plan's learning goal)
- Outer wrapper navigated by pinned `ldb` namespace; inner `lt*` typed elements navigated by local name to stay tolerant of Darwin minor-version namespace bumps
- Origin/destination: take first `<location>` only (joined services list multiple) — keeps board compact
- NRCC severity read from child element first, then attribute fallback — schema versions disagree on placement
- SOAP fault extraction on non-2xx: parse body, surface `faultstring` instead of bare HTTP status
- Times kept as raw strings (matches Phase 2 decision — Darwin returns "On time" / HH:mm mixed forms)
- Token via env var for Phase 3 only; Phase 5 moves to `dotnet user-secrets`

**Verified:**

- Compile clean (file-lock copy error only — running MCP server holds `DarwinMcp.exe`, identical to Phase 2)
- Pending: live `--probe BDM STP` run with real token to confirm parse paths match actual Darwin payload

**Open questions for next phase:**

- `serviceID` vs `rid` — Darwin returns both; confirm which `GetServiceDetails` actually wants
- Are there services where `std`/`etd` are absent (e.g. arrivals-only)? May need null guards rather than `?? string.Empty`
- NRCC `Message` body is HTML in some feeds — strip tags before returning to LLM, or pass through?

---

## Phase 4 — Complete

**Built:**

- `Darwin/SoapEnvelopes.cs` — added `GetServiceDetails` envelope builder (mirrors departure-board pattern, `SecurityElement.Escape` on `serviceID`)
- `Darwin/DarwinClient.cs` — added `GetServiceDetailsAsync` + `ParseServiceDetails`; flattens `previousCallingPoints` + origin row + `subsequentCallingPoints` into a single ordered route
- `Data/stations.csv` — ~95 common UK stations bundled as content (`CopyToOutputDirectory=PreserveNewest`)
- `Data/StationLookup.cs` — singleton, loads CSV once from `AppContext.BaseDirectory`, ranks results prefix > contains, caps at 10
- `Tools/*` — all four tools converted to instance classes with constructor injection (`DarwinClient` / `StationLookup`); CRS validated up front; try/catch around calls returns LLM-friendly strings for `DarwinApiException`, `HttpRequestException`, `TaskCanceledException`
- `Tools/DisruptionsTool.cs` — calls `GetDepartureBoardAsync(crs, null, numRows: 1)` to pull NRCC cheaply (Darwin LDBWS lite has no messages-only endpoint)
- `Program.cs` — DI registers named `HttpClient "darwin"` (15s timeout, factory-pooled), `DarwinClient` singleton with token from `Configuration["Darwin:Token"]` → `DARWIN_TOKEN` env var fallback, `StationLookup` singleton
- `DarwinMcp.csproj` — added `Microsoft.Extensions.Http` 10.0.7 (for `AddHttpClient`/`IHttpClientFactory`); `<None Update="Data/stations.csv" CopyToOutputDirectory=PreserveNewest>`

**Design decisions:**

- Tools return `Task<object>` (DTO on success, `string` on error) rather than throwing — keeps the LLM's context unbroken when Darwin or the network fails, and lets the error message hint at next steps (`lookup_station`, retry, re-fetch board for expired serviceId)
- Empty token tolerated at startup (logged as failure only on first SOAP call) so `tools/list` still succeeds before token is configured
- `StationLookup` uses naive `LastIndexOf(',')` CSV split — fine while no station name contains a comma; bring in a parser package if that ever changes
- `numRows` clamped 1–20 in `DeparturesTool` to keep responses LLM-sized and match Darwin's documented max
- `HttpClient` lifetime managed by `IHttpClientFactory` (named client `"darwin"`) rather than constructed per-request — avoids socket exhaustion, avoids the singleton-stale-DNS problem
- Probe path in `Program.cs` retained: still constructs `DarwinClient` directly so the MCP DI graph isn't required for raw API exploration

**Verified:**

- `dotnet build` clean (0 warnings, 0 errors)
- Pending: live Claude Code call to confirm `tools/list` still shows 5 tools and a real `get_departures("BDM", "STP")` round-trips with a valid `Darwin:Token`

**Open questions for next phase:**

- Should `lookup_station` accept a `crs` parameter too (reverse lookup) for when the LLM wants to expand a code into a name?
- Which stations to ship in `stations.csv` — current ~95 covers main InterCity + London terminals; full NR list is ~2,500. Embed full list, or keep curated subset?
- Empty-token startup is permissive; Phase 5 should probably log a warning at startup when the token is missing rather than only failing on first call.

---

## Phase 5 — Complete

**Built:**

- `appsettings.json` — Serilog config: file sink only, `logs/darwin-.log` daily rolling, 7-day retention, `Microsoft`/`HttpClient` overrides at Warning
- `Program.cs` — explicit `AddJsonFile` from `AppContext.BaseDirectory` (Claude Code may spawn with different CWD); `Directory.SetCurrentDirectory(BaseDirectory)` so Serilog's relative `logs/` path resolves next to the binary; `Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(...)`, attached via `AddSerilog(dispose: true)` after `ClearProviders()` — file sink only, stdout stays JSON-RPC clean
- `Tools/EchoTool.cs` — deleted (Phase 1 sanity check no longer needed); registration removed from `Program.cs`
- `DarwinMcp.csproj` — `Serilog.Extensions.Hosting` 10.0.0, `Serilog.Settings.Configuration` 10.0.0, `Serilog.Sinks.File` 7.0.0; `<None Update="appsettings.json" CopyToOutputDirectory=PreserveNewest>`
- `README.md` (repo root) — tools table, CRS format, `dotnet user-secrets` setup, MCP client config snippet, logging path/rolling explained, probe-mode usage, limitations

**Design decisions:**

- Token stays in `dotnet user-secrets` (already wired in Phase 4 via `Configuration["Darwin:Token"]`); no startup change needed for Phase 5 — checkpoint just confirmed `AddUserSecrets<Program>` is loaded explicitly because MCP launches as Production env
- Serilog config in `appsettings.json` rather than code so log levels can be tuned without rebuilding
- File sink only — no console sink. Adding one would corrupt the JSON-RPC stream on stdout. README calls this out explicitly to prevent future regressions
- `Directory.SetCurrentDirectory(BaseDirectory)` chosen over an absolute path in the sink config so logs follow the binary in any deployment layout
- `CLAUDE.md` (repo root) already covers tool guide, CRS format, and Darwin limitations — left as-is, no duplication into README

**Verified:**

- `dotnet build` clean (0 warnings, 0 errors) after killing the running MCP server holding `DarwinMcp.exe` lock
- Live Claude Code call after `/mcp` reconnect: `check_disruptions("PRE")` and `get_departures("PRE","CRL")` both round-trip — confirms Serilog wiring + dropped EchoTool didn't break the host

**Open questions for next phase:**

- Whether to add startup warning when `Darwin:Token` is empty (carry-over from Phase 4 — still permissive at startup)
- Caching layer (`IMemoryCache`, ~60s TTL on departure boards) is the most useful Phase 6 stretch goal given iterative Claude conversations re-fetching the same board
