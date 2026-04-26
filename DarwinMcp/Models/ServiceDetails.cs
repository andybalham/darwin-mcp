namespace DarwinMcp.Models;

// Full picture of a single service — returned by get_service_details.
// CallingPoints is the through-route in order; the service's current
// location can be inferred by the LLM from which points have ActualTime
// set vs only EstimatedTime.
public record ServiceDetails(
    string ServiceId,
    string Operator,
    string Origin,
    string Destination,
    string ScheduledDeparture,
    string? EstimatedDeparture,
    string? Platform,
    bool IsCancelled,
    string? DelayReason,
    IReadOnlyList<CallingPoint> CallingPoints);
