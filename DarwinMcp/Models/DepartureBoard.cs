namespace DarwinMcp.Models;

// Top-level response for get_departures. Wraps the list of services with
// the board's own metadata (which station, when generated) plus any NRCC
// messages currently attached to the station — the LLM should mention
// these when reporting departures, so they live alongside the services.
public record DepartureBoard(
    string StationName,
    string Crs,
    string GeneratedAt,
    IReadOnlyList<ServiceSummary> Services,
    IReadOnlyList<NrccMessage> Messages);
