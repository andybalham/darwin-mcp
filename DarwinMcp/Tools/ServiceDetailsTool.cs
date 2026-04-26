using DarwinMcp.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// Phase 2 stub. Pairs with DeparturesTool: the LLM sees a ServiceId on the
// board and uses it here to drill into calling pattern and delay reason.
[McpServerToolType]
public class ServiceDetailsTool
{
    [McpServerTool, Description(
        "Get full details for a specific train service: every calling point, " +
        "scheduled and estimated times, and any delay reason. " +
        "Use the serviceId from a get_departures response.")]
    public static ServiceDetails GetServiceDetails(
        [Description("Opaque service identifier returned by get_departures (not human-meaningful)")]
            string serviceId)
    {
        return new ServiceDetails(
            ServiceId: serviceId,
            Operator: "Thameslink",
            Origin: "Bedford",
            Destination: "Brighton",
            ScheduledDeparture: "08:42",
            EstimatedDeparture: "On time",
            Platform: "2",
            IsCancelled: false,
            DelayReason: null,
            CallingPoints:
            [
                new CallingPoint("Bedford", "BDM", "08:42", "On time", null),
                new CallingPoint("Luton", "LUT", "08:58", "On time", null),
                new CallingPoint("Luton Airport Parkway", "LTN", "09:02", "On time", null),
                new CallingPoint("St Pancras International", "STP", "09:30", "On time", null),
                new CallingPoint("Brighton", "BTN", "10:45", "On time", null)
            ]);
    }
}
