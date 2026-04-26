namespace DarwinMcp.Models;

// One stop on a service's route. Darwin returns scheduled (ST) and estimated
// (ET) times as separate strings — "On time", "Cancelled", or HH:mm. We keep
// the raw strings here because the LLM consuming this data finds "On time"
// more useful than a parsed DateTime + boolean flag.
public record CallingPoint(
    string StationName,
    string Crs,
    string ScheduledTime,
    string? EstimatedTime,
    string? ActualTime);
