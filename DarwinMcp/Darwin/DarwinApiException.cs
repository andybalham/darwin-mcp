namespace DarwinMcp.Darwin;

// Thrown when Darwin returns a SOAP Fault, an HTTP non-success, or when the
// response XML is shaped in a way we can't parse. Phase 4 catches this in
// the tool layer and turns it into an LLM-friendly error string.
public sealed class DarwinApiException : Exception
{
    public DarwinApiException(string message) : base(message) { }
    public DarwinApiException(string message, Exception inner) : base(message, inner) { }
}
