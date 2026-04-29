using DarwinMcp.Models;

namespace DarwinMcp.Data;

// Loads the bundled stations.csv once at startup and serves CRS lookups
// against it. Registered as a singleton in DI so the file isn't re-read
// per tool call. Avoids any Darwin round-trip — station codes are static.
//
// CSV format: header row "Name,Crs" then one row per station. Names must
// not contain commas; if we add such stations later, switch to a proper
// CSV parser package. For now keep zero deps.
public sealed class StationLookup
{
    private readonly IReadOnlyList<StationMatch> _stations;

    public StationLookup()
    {
        // AppContext.BaseDirectory is the folder the runtime loaded from —
        // matches where MSBuild copies content files (Data/stations.csv).
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "stations.csv");
        if (!File.Exists(path))
        {
            // Fail soft: empty list means lookup_station returns nothing
            // rather than crashing the whole server.
            _stations = Array.Empty<StationMatch>();
            return;
        }

        var rows = new List<StationMatch>();
        // Skip header row (index 0). Trim each line; ignore blanks.
        var lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            var comma = line.LastIndexOf(',');
            if (comma <= 0 || comma == line.Length - 1) continue;
            var name = line[..comma].Trim();
            var crs = line[(comma + 1)..].Trim().ToUpperInvariant();
            if (crs.Length != 3) continue;
            rows.Add(new StationMatch(name, crs));
        }
        _stations = rows;
    }

    // Ranking: prefix matches before contains matches, alphabetical within
    // each group. Cap to 10 results so the LLM gets a usable shortlist
    // without a wall of text for ambiguous queries like "London".
    public IReadOnlyList<StationMatch> Find(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<StationMatch>();
        var q = query.Trim();

        var prefix = _stations
            .Where(s => s.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var contains = _stations
            .Where(s => !s.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                     && s.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

        return prefix.Concat(contains).Take(10).ToArray();
    }
}
