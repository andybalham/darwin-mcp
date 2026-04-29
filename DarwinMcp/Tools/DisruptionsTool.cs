using DarwinMcp.Darwin;
using DarwinMcp.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// Phase 4: NRCC messages come back inside the departure board response
// (Darwin doesn't expose a separate "just the messages" endpoint on the
// LDBWS lite API). We request a tiny board (numRows=1) purely to pull the
// nrccMessages collection — cheaper than a full 10-row board.
[McpServerToolType]
public class DisruptionsTool
{
    private readonly DarwinClient _darwin;

    public DisruptionsTool(DarwinClient darwin) => _darwin = darwin;

    [McpServerTool, Description(
        "Check active National Rail disruption and information messages " +
        "(NRCC) for a station. Use this when the user asks about delays, " +
        "engineering works, or service alterations affecting a station.")]
    public async Task<object> CheckDisruptions(
        [Description("Station CRS code (3-letter uppercase, e.g. STP for St Pancras)")]
            string crs)
    {
        if (crs.Length != 3 || !crs.All(char.IsLetter))
            return $"Invalid CRS code '{crs}'. Expected 3 letters. Use lookup_station to find one.";

        try
        {
            // numRows=1 minimises bandwidth — we only want Messages. Darwin
            // returns NRCC regardless of how many service rows are asked for.
            var board = await _darwin.GetDepartureBoardAsync(crs.ToUpperInvariant(), toCrs: null, numRows: 1);
            return board.Messages.Count == 0
                ? (object)$"No active NRCC messages for {board.StationName} ({board.Crs})."
                : board.Messages;
        }
        catch (DarwinApiException ex)
        {
            return $"Darwin API error: {ex.Message}. Verify the CRS code or try lookup_station.";
        }
        catch (HttpRequestException ex)
        {
            return $"Could not reach the Darwin API ({ex.Message}). Check the network and retry.";
        }
        catch (TaskCanceledException)
        {
            return "Darwin API request timed out. The service may be slow; retry shortly.";
        }
    }
}
