using DarwinMcp.Darwin;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// Phase 4: real Darwin GetServiceDetails. Pairs with DeparturesTool —
// LLM takes a serviceId from a board row and drills in for the full
// calling pattern.
[McpServerToolType]
public class ServiceDetailsTool
{
    private readonly DarwinClient _darwin;

    public ServiceDetailsTool(DarwinClient darwin) => _darwin = darwin;

    [McpServerTool, Description(
        "Get full details for a specific train service: every calling point, " +
        "scheduled and estimated times, and any delay reason. " +
        "Use the serviceId from a get_departures response.")]
    public async Task<object> GetServiceDetails(
        [Description("Opaque service identifier returned by get_departures (not human-meaningful)")]
            string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return "serviceId is required. Pull one from a get_departures response first.";

        try
        {
            return await _darwin.GetServiceDetailsAsync(serviceId);
        }
        catch (DarwinApiException ex)
        {
            // Most common case: serviceId expired (Darwin invalidates them
            // once the service has departed long enough ago). Hint that
            // the user re-fetches the board.
            return $"Darwin API error: {ex.Message}. Service IDs expire — re-run get_departures and retry.";
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
