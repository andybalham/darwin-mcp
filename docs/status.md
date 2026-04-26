# Darwin MCP — Implementation Status

Tracks progress against [darwin-mcp-implementation-plan.md](./darwin-mcp-implementation-plan.md).

| Phase | Status | Notes |
|---|---|---|
| 1 — MCP server fundamentals | ✅ Complete | Echo tool live, verified via Claude Code |
| 2 — Domain model + tool stubs | ✅ Complete | 4 stub tools + DTOs registered |
| 3 — Darwin SOAP client | ✅ Complete | `DarwinClient` + `--probe` harness; not yet wired into MCP tools |
| 4 — Wire real API to tools | ⏳ Not started | |
| 5 — Config, secrets, polish | ⏳ Not started | |
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

## Phase 4 — Next up

Replace stub returns in `Tools/*` with `DarwinClient` calls via constructor injection. Register `DarwinClient` (+ `HttpClient`) in DI. Wrap calls in try/catch returning LLM-friendly error strings. Wire `lookup_station` to bundled `Data/stations.csv`.
