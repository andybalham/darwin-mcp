namespace DarwinMcp.Models;

// One row on a departure board. Just enough info for an LLM to answer
// "what's the next train to X" — full calling pattern lives behind
// get_service_details (keyed by ServiceId) to keep board responses small.
//
// ServiceId is Darwin's opaque token (RID-like). It's not human-meaningful
// but is required to fetch details, so we surface it.
public record ServiceSummary(
    string ServiceId,
    string Operator,
    string Origin,
    string Destination,
    string ScheduledDeparture,
    string? EstimatedDeparture,
    string? Platform,
    bool IsCancelled,
    string? DelayReason);
