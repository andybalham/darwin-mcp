namespace DarwinMcp.Models;

// NRCC = National Rail Communication Centre. Free-text disruption/info
// messages Darwin attaches to a station board (engineering works, delays,
// service alterations). Severity is Darwin's own enum; we keep it as int
// to avoid coupling to its exact values until Phase 3 confirms them.
public record NrccMessage(
    string Severity,
    string Message);
