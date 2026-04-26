using DarwinMcp.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// Phase 2 stub: returns hardcoded sample data shaped like a real Darwin
// response so we can iterate on tool descriptions and DTO shape before
// adding SOAP complexity (Phase 3/4).
//
// Why static methods? No dependencies yet. Phase 4 swaps these for instance
// methods with DarwinClient injected via constructor — at which point the
// reflection scanner picks up the instance and resolves the dependency.
[McpServerToolType]
public class DeparturesTool
{
    // Tool description is written for the *LLM*: tell it what it can do and
    // when to pick it. Parameter descriptions are equally important — the
    // model uses them to choose argument values. CRS code constraints
    // (3 letters, uppercase) belong here so the LLM doesn't pass "BEDFORD".
    [McpServerTool, Description(
        "Get the live departure board for a UK National Rail station. " +
        "Optionally filter to services calling at a specific destination. " +
        "Returns the next departing services with platform, operator, and delay info.")]
    public static DepartureBoard GetDepartures(
        [Description("Origin station CRS code (3-letter uppercase, e.g. BDM for Bedford, STP for St Pancras)")]
            string fromCrs,
        [Description("Optional destination CRS code to filter services calling at that station")]
            string? toCrs = null,
        [Description("Maximum number of services to return (default 10, max 20)")]
            int numRows = 10)
    {
        // Sample data only — shape mirrors what Phase 4 will produce from
        // real Darwin XML so callers (LLM + downstream tools) don't break
        // when we wire the real client in.
        return new DepartureBoard(
            StationName: "Bedford",
            Crs: fromCrs.ToUpperInvariant(),
            GeneratedAt: "2026-04-26T08:30:00",
            Services:
            [
                new ServiceSummary(
                    ServiceId: "stub-service-1",
                    Operator: "Thameslink",
                    Origin: "Bedford",
                    Destination: toCrs is null ? "Brighton" : "St Pancras International",
                    ScheduledDeparture: "08:42",
                    EstimatedDeparture: "On time",
                    Platform: "2",
                    IsCancelled: false,
                    DelayReason: null),
                new ServiceSummary(
                    ServiceId: "stub-service-2",
                    Operator: "East Midlands Railway",
                    Origin: "Bedford",
                    Destination: "London St Pancras",
                    ScheduledDeparture: "08:51",
                    EstimatedDeparture: "08:55",
                    Platform: "1",
                    IsCancelled: false,
                    DelayReason: "This train has been delayed by a signalling problem")
            ],
            Messages: []);
    }
}
