using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using DarwinMcp.Models;

namespace DarwinMcp.Darwin;

// Standalone Darwin SOAP client. Knows nothing about MCP — Phase 4 will
// inject this into the tool classes via DI. Keeping the API boundary here
// means tool code stays a thin translation layer.
//
// Why raw HttpClient + XDocument rather than a generated WCF/svcutil proxy?
//  * Zero codegen, zero hidden config — every byte on the wire is visible.
//  * Darwin's WSDL is large; the generated surface dwarfs what we actually use.
//  * Learning goal of the plan: see SOAP mechanics, not hide them.
public sealed class DarwinClient
{
    // Production LDBWS endpoint. Note `ldb12.asmx` aligns with the 2021-11-01
    // schema referenced in our SOAP envelope. Older endpoints (ldb11, ldb9...)
    // exist but use earlier ldb namespaces.
    private const string Endpoint = "https://lite.realtime.nationalrail.co.uk/OpenLDBWS/ldb12.asmx";

    // Namespaces we'll need for response parsing. The response wraps payload
    // elements in `ldb` (operation namespace) and `lt*` (typed elements). We
    // only pin `ldb`; for the typed elements we navigate by local name to
    // stay tolerant of Darwin bumping the lt namespace minor versions.
    private static readonly XNamespace Soap = SoapEnvelopes.SoapNamespace;
    private static readonly XNamespace Ldb = SoapEnvelopes.LdbNamespace;

    private readonly HttpClient _http;
    private readonly string _token;

    public DarwinClient(HttpClient http, string token)
    {
        _http = http;
        _token = token;
    }

    public async Task<DepartureBoard> GetDepartureBoardAsync(
        string fromCrs,
        string? toCrs,
        int numRows,
        CancellationToken ct = default)
    {
        var envelope = SoapEnvelopes.GetDepartureBoard(_token, fromCrs, toCrs, numRows);
        var xml = await PostAsync(envelope, soapAction: "http://thalesgroup.com/RTTI/2021-11-01/ldb/GetDepartureBoard", ct);
        return ParseDepartureBoard(xml, fromCrs);
    }

    // Single POST helper. SOAP 1.1 expects:
    //   * Content-Type: text/xml; charset=utf-8
    //   * SOAPAction header (Darwin tolerates empty, but sending the right one
    //     is correct and helps any debugging proxy in the middle).
    private async Task<XDocument> PostAsync(string envelope, string soapAction, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(envelope, Encoding.UTF8)
        };
        // StringContent's mediaType ctor rejects parameters, so set the full
        // header value directly to keep `charset=utf-8` on the wire.
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        // SOAPAction MUST be quoted per SOAP 1.1.
        req.Headers.Add("SOAPAction", $"\"{soapAction}\"");

        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            // 500 from Darwin usually carries a SOAP Fault in the body — surface
            // its faultstring if present, else the raw status.
            var fault = TryExtractFault(body);
            throw new DarwinApiException(
                fault is null
                    ? $"Darwin HTTP {(int)res.StatusCode}: {res.ReasonPhrase}"
                    : $"Darwin SOAP fault: {fault}");
        }

        try
        {
            return XDocument.Parse(body);
        }
        catch (Exception ex)
        {
            throw new DarwinApiException("Failed to parse Darwin response as XML.", ex);
        }
    }

    private static string? TryExtractFault(string body)
    {
        try
        {
            var doc = XDocument.Parse(body);
            // SOAP 1.1: Body/Fault/faultstring (faultstring is unqualified).
            return doc.Descendants(Soap + "Fault")
                      .Elements()
                      .FirstOrDefault(e => e.Name.LocalName == "faultstring")
                      ?.Value;
        }
        catch
        {
            return null;
        }
    }

    // Shape of a GetDepartureBoardResponse (abbreviated):
    //
    //   soap:Envelope/soap:Body/
    //     ldb:GetDepartureBoardResponse/
    //       ldb:GetStationBoardResult/
    //         lt4:generatedAt
    //         lt4:locationName
    //         lt4:crs
    //         lt4:nrccMessages/lt:message[]                (NRCC messages, raw HTML)
    //         lt8:trainServices/lt8:service[]              (the rows)
    //           lt4:std / lt4:etd                          (scheduled / estimated times)
    //           lt4:platform / lt4:operator
    //           lt4:serviceID                              (opaque token used by GetServiceDetails)
    //           lt5:isCancelled / lt4:delayReason
    //           lt5:origin/lt4:location/lt4:locationName
    //           lt5:destination/lt4:location/lt4:locationName
    //
    // The `lt*` namespaces vary by Darwin minor revisions, so navigate by
    // local name. `ldb` is stable per-endpoint and we pin it for the outer
    // response wrapper.
    private static DepartureBoard ParseDepartureBoard(XDocument doc, string fromCrs)
    {
        var result = doc.Descendants(Ldb + "GetStationBoardResult").FirstOrDefault()
            ?? throw new DarwinApiException("Response missing GetStationBoardResult.");

        string Local(string name) => result.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;

        var stationName = Local("locationName");
        var crs = Local("crs");
        var generatedAt = Local("generatedAt");

        var services = result.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "trainServices")
            ?.Elements()
            .Where(e => e.Name.LocalName == "service")
            .Select(ParseService)
            .ToList()
            ?? new List<ServiceSummary>();

        var messages = result.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "nrccMessages")
            ?.Elements()
            .Where(e => e.Name.LocalName == "message")
            // NRCC `message` carries severity as an attribute on the element in
            // some schema versions and as a child in others. Try child first,
            // fall back to attribute, default to "0".
            .Select(m => new NrccMessage(
                Severity: m.Elements().FirstOrDefault(x => x.Name.LocalName == "severity")?.Value
                          ?? m.Attribute("severity")?.Value
                          ?? "0",
                Message: m.Value))
            .ToList()
            ?? new List<NrccMessage>();

        return new DepartureBoard(
            StationName: string.IsNullOrEmpty(stationName) ? fromCrs : stationName,
            Crs: string.IsNullOrEmpty(crs) ? fromCrs : crs,
            GeneratedAt: generatedAt,
            Services: services,
            Messages: messages);
    }

    private static ServiceSummary ParseService(XElement svc)
    {
        string? Child(string name) => svc.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

        // origin/destination are wrapped: <origin><location><locationName>...
        // Darwin can list multiple origins/destinations for joined services;
        // we surface the first only — keeps the board compact.
        string? FirstLocationName(string wrapper) =>
            svc.Elements().FirstOrDefault(e => e.Name.LocalName == wrapper)
               ?.Elements().FirstOrDefault(e => e.Name.LocalName == "location")
               ?.Elements().FirstOrDefault(e => e.Name.LocalName == "locationName")
               ?.Value;

        var isCancelled = bool.TryParse(Child("isCancelled"), out var cancelled) && cancelled;

        return new ServiceSummary(
            ServiceId: Child("serviceID") ?? string.Empty,
            Operator: Child("operator") ?? string.Empty,
            Origin: FirstLocationName("origin") ?? string.Empty,
            Destination: FirstLocationName("destination") ?? string.Empty,
            ScheduledDeparture: Child("std") ?? string.Empty,
            EstimatedDeparture: Child("etd"),
            Platform: Child("platform"),
            IsCancelled: isCancelled,
            DelayReason: Child("delayReason"));
    }
}
