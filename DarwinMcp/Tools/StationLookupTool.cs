using DarwinMcp.Data;
using DarwinMcp.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// Phase 4: backed by the bundled stations.csv via StationLookup (singleton).
// No Darwin call — CRS codes are static, so we ship them in the binary.
[McpServerToolType]
public class StationLookupTool
{
    private readonly StationLookup _lookup;

    public StationLookupTool(StationLookup lookup) => _lookup = lookup;

    [McpServerTool, Description(
        "Find the 3-letter CRS code for a UK railway station by name. " +
        "Use this first when the user mentions a station by name — the " +
        "other tools (get_departures, check_disruptions) require CRS codes. " +
        "Returns up to 10 matches, prefix matches first; pick the most likely one.")]
    public IReadOnlyList<StationMatch> LookupStation(
        [Description("Full or partial station name, case-insensitive (e.g. 'bedford', 'luton airport')")]
            string query)
        => _lookup.Find(query);
}
