namespace DarwinMcp.Models;

// Result row for lookup_station. The LLM uses this to disambiguate user
// phrasing ("Luton Airport") into a CRS code before calling other tools.
// No Darwin call needed at runtime — Phase 4 will load this from a
// bundled stations.csv.
public record StationMatch(
    string Name,
    string Crs);
