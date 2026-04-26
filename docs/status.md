# Darwin MCP — Implementation Status

Tracks progress against [darwin-mcp-implementation-plan.md](./darwin-mcp-implementation-plan.md).

| Phase | Status | Notes |
|---|---|---|
| 1 — MCP server fundamentals | ✅ Complete | Echo tool live, verified via Claude Code |
| 2 — Domain model + tool stubs | ⏳ Not started | |
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

## Phase 2 — Next up

Design domain model + stub tools before any SOAP code. Target tools per plan:

| Tool | Params | Returns |
|---|---|---|
| `get_departures` | `fromCrs`, `toCrs?`, `numRows?` | Departing services |
| `get_service_details` | `serviceId` | Calling points, delays |
| `check_disruptions` | `crs` | NRCC messages |
| `lookup_station` | `query` | CRS code lookup |
