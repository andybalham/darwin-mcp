using DarwinMcp.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// Phase 2 stub. The other tools take CRS codes, but users say station
// *names*. This tool bridges that gap so the LLM can look up "Luton" → LTN
// before calling get_departures. Phase 4 will load a real CSV; for now
// the stub returns a couple of plausible matches.
[McpServerToolType]
public class StationLookupTool
{
    [McpServerTool, Description(
        "Find the 3-letter CRS code for a UK railway station by name. " +
        "Use this first when the user mentions a station by name — the " +
        "other tools (get_departures, check_disruptions) require CRS codes. " +
        "Returns a list of possible matches; pick the most likely one.")]
    public static IReadOnlyList<StationMatch> LookupStation(
        [Description("Full or partial station name, case-insensitive (e.g. 'bedford', 'luton airport')")]
            string query)
    {
        // Tiny in-memory sample; Phase 4 swaps for stations.csv lookup.
        var stations = new[]
        {
            new StationMatch("Bedford", "BDM"),
            new StationMatch("London St Pancras International", "STP"),
            new StationMatch("Luton", "LUT"),
            new StationMatch("Luton Airport Parkway", "LTN"),
            new StationMatch("Brighton", "BTN")
        };

        return stations
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
