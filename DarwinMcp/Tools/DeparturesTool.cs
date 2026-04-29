using DarwinMcp.Darwin;
using DarwinMcp.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DarwinMcp.Tools;

// Phase 4: real Darwin call via injected DarwinClient. Tool stays thin —
// it just translates the DTO and turns API/network failures into
// natural-language strings the LLM can relay to the user.
//
// Why DTO-or-string return type? MCP tools return text content; throwing
// here would surface as a tool error to the model. Returning a friendly
// message keeps the conversation flowing and gives the LLM context to
// suggest next steps (e.g. retry, try lookup_station, etc.).
[McpServerToolType]
public class DeparturesTool
{
    private readonly DarwinClient _darwin;

    // Constructor injection: the DI container resolves DarwinClient from
    // the registration in Program.cs. WithTools<DeparturesTool>() wires
    // this up via the MCP SDK's reflection scan.
    public DeparturesTool(DarwinClient darwin) => _darwin = darwin;

    [McpServerTool, Description(
        "Get the live departure board for a UK National Rail station. " +
        "Optionally filter to services calling at a specific destination. " +
        "Returns the next departing services with platform, operator, and delay info.")]
    public async Task<object> GetDepartures(
        [Description("Origin station CRS code (3-letter uppercase, e.g. BDM for Bedford, STP for St Pancras)")]
            string fromCrs,
        [Description("Optional destination CRS code to filter services calling at that station")]
            string? toCrs = null,
        [Description("Maximum number of services to return (default 10, max 20)")]
            int numRows = 10)
    {
        // Defensive normalisation: LLMs sometimes pass lower-case or padded
        // CRS. Darwin requires 3-letter upper-case; reject early so the
        // user sees a clear error instead of a SOAP fault.
        if (!IsCrs(fromCrs))
            return $"Invalid CRS code '{fromCrs}'. Expected 3 letters (e.g. BDM). Use lookup_station to find one.";
        if (toCrs is not null && !IsCrs(toCrs))
            return $"Invalid destination CRS code '{toCrs}'. Expected 3 letters or omit. Use lookup_station to find one.";

        try
        {
            return await _darwin.GetDepartureBoardAsync(
                fromCrs.ToUpperInvariant(),
                toCrs?.ToUpperInvariant(),
                Math.Clamp(numRows, 1, 20));
        }
        catch (DarwinApiException ex)
        {
            // SOAP-level problem: bad token, unknown CRS, schema mismatch.
            return $"Darwin API error: {ex.Message}. Verify the CRS code or try lookup_station.";
        }
        catch (HttpRequestException ex)
        {
            // Network/transport: DNS, TLS, connection reset, timeout.
            return $"Could not reach the Darwin API ({ex.Message}). Check the network and retry.";
        }
        catch (TaskCanceledException)
        {
            // HttpClient timeout surfaces as TaskCanceledException with no inner.
            return "Darwin API request timed out. The service may be slow; retry shortly.";
        }
    }

    private static bool IsCrs(string s) =>
        s.Length == 3 && s.All(char.IsLetter);
}
