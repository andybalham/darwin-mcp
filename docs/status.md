# Darwin MCP — Implementation Status

Tracks progress against [darwin-mcp-implementation-plan.md](./darwin-mcp-implementation-plan.md).

| Phase | Status | Notes |
|---|---|---|
| 1 — MCP server fundamentals | ✅ Complete | Echo tool live, verified via Claude Code |
| 2 — Domain model + tool stubs | ✅ Complete | 4 stub tools + DTOs registered |
| 3 — Darwin SOAP client | ⏳ Not started | |
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

## Phase 3 — Next up

Stand-alone `DarwinClient` with raw `HttpClient` + `XDocument` against real Darwin SOAP. No MCP wiring yet — scratch harness in `Program.cs` (or separate test project) to dump real XML responses.
