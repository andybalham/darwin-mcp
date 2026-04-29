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
        var xml = await PostAsync(envelope, soapAction: "http://thalesgroup.com/RTTI/2012-01-13/ldb/GetDepartureBoard", ct);
        return ParseDepartureBoard(xml, fromCrs);
    }

    // Drill into a single service. ServiceId is the opaque token from
    // GetDepartureBoard's <serviceID> element.
    public async Task<ServiceDetails> GetServiceDetailsAsync(
        string serviceId,
        CancellationToken ct = default)
    {
        var envelope = SoapEnvelopes.GetServiceDetails(_token, serviceId);
        var xml = await PostAsync(envelope, soapAction: "http://thalesgroup.com/RTTI/2012-01-13/ldb/GetServiceDetails", ct);
        return ParseServiceDetails(xml, serviceId);
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

    // Shape of GetServiceDetailsResponse (abbreviated):
    //
    //   ldb:GetServiceDetailsResponse/
    //     ldb:GetServiceDetailsResult/
    //       lt4:generatedAt
    //       lt4:serviceType / lt4:locationName / lt4:crs
    //       lt4:operator / lt4:platform
    //       lt4:sta / lt4:eta / lt4:std / lt4:etd
    //       lt4:delayReason / lt4:isCancelled
    //       lt7:previousCallingPoints/lt7:callingPointList/lt7:callingPoint[]
    //       lt7:subsequentCallingPoints/lt7:callingPointList/lt7:callingPoint[]
    //         lt7:locationName / lt7:crs / lt7:st / lt7:et / lt7:at
    //
    // For a board-driven query (departures from origin), Darwin only fills
    // subsequentCallingPoints. We concatenate previous (if present) + the
    // origin row + subsequent so the LLM gets a single ordered list.
    private static ServiceDetails ParseServiceDetails(XDocument doc, string serviceId)
    {
        var result = doc.Descendants(Ldb + "GetServiceDetailsResult").FirstOrDefault()
            ?? throw new DarwinApiException("Response missing GetServiceDetailsResult.");

        string? Child(string name) => result.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

        var originName = Child("locationName") ?? string.Empty;
        var originCrs = Child("crs") ?? string.Empty;
        var op = Child("operator") ?? string.Empty;
        var platform = Child("platform");
        var std = Child("std") ?? string.Empty;
        var etd = Child("etd");
        var isCancelled = bool.TryParse(Child("isCancelled"), out var cancelled) && cancelled;
        var delayReason = Child("delayReason");

        // Build the calling points list. Darwin nests:
        //   <subsequentCallingPoints>
        //     <callingPointList>           ← may have multiple lists for joined/divided services
        //       <callingPoint>...</callingPoint>
        //     </callingPointList>
        //   </subsequentCallingPoints>
        // For a non-joined service we expect one list; we flatten all of them
        // for safety.
        IEnumerable<XElement> CallingPointsIn(string wrapper) =>
            result.Elements()
                  .Where(e => e.Name.LocalName == wrapper)
                  .SelectMany(e => e.Elements().Where(x => x.Name.LocalName == "callingPointList"))
                  .SelectMany(l => l.Elements().Where(c => c.Name.LocalName == "callingPoint"));

        CallingPoint ToCp(XElement cp)
        {
            string? V(string n) => cp.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value;
            return new CallingPoint(
                StationName: V("locationName") ?? string.Empty,
                Crs: V("crs") ?? string.Empty,
                ScheduledTime: V("st") ?? string.Empty,
                EstimatedTime: V("et"),
                ActualTime: V("at"));
        }

        var prev = CallingPointsIn("previousCallingPoints").Select(ToCp).ToList();
        var subs = CallingPointsIn("subsequentCallingPoints").Select(ToCp).ToList();

        // Synthesise a calling-point row for the origin itself so the LLM
        // sees a single ordered through-route. The board query only knows
        // scheduled/estimated departure (no `at`), which mirrors what Darwin
        // exposes for a current-station row.
        var originPoint = new CallingPoint(
            StationName: originName,
            Crs: originCrs,
            ScheduledTime: std,
            EstimatedTime: etd,
            ActualTime: null);

        var route = new List<CallingPoint>(prev.Count + 1 + subs.Count);
        route.AddRange(prev);
        route.Add(originPoint);
        route.AddRange(subs);

        // Origin/Destination labels: first calling point is the route origin,
        // last is the destination — works for both forward (subsequent only)
        // and through-train (with previous) cases.
        var origin = route.Count > 0 ? route[0].StationName : originName;
        var destination = route.Count > 0 ? route[^1].StationName : originName;

        return new ServiceDetails(
            ServiceId: serviceId,
            Operator: op,
            Origin: origin,
            Destination: destination,
            ScheduledDeparture: std,
            EstimatedDeparture: etd,
            Platform: platform,
            IsCancelled: isCancelled,
            DelayReason: delayReason,
            CallingPoints: route);
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
