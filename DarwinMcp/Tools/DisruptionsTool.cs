using DarwinMcp.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// Phase 2 stub. NRCC messages are also embedded in DepartureBoard, but a
// dedicated tool lets the LLM check disruption *without* pulling a full
// board — cheaper and clearer when the user asks "any problems at STP?".
[McpServerToolType]
public class DisruptionsTool
{
    [McpServerTool, Description(
        "Check active National Rail disruption and information messages " +
        "(NRCC) for a station. Use this when the user asks about delays, " +
        "engineering works, or service alterations affecting a station.")]
    public static IReadOnlyList<NrccMessage> CheckDisruptions(
        [Description("Station CRS code (3-letter uppercase, e.g. STP for St Pancras)")]
            string crs)
    {
        return
        [
            new NrccMessage(
                Severity: "Major",
                Message: $"Trains at {crs.ToUpperInvariant()} are subject to delays of up to 30 minutes due to a signalling failure between Bedford and St Pancras."),
            new NrccMessage(
                Severity: "Minor",
                Message: "Engineering works will affect services on Sunday 26 April. Replacement buses will operate.")
        ];
    }
}
