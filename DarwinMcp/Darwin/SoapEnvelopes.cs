using System.Security;

namespace DarwinMcp.Darwin;

// Hand-built SOAP envelope strings. Plan deliberately avoids WCF / svcutil
// proxy generation at this stage so the wire format stays visible. Each
// builder returns a complete `<soap:Envelope>` ready to POST as the body.
//
// Namespace prefixes used:
//   soap → SOAP 1.1 envelope
//   typ  → Darwin token header type
//   ldb  → Darwin LDB operations (2021-11-01 schema)
//
// The token sits in the SOAP header; the operation-specific request sits in
// the body. Order of body elements matters — Darwin's schema is sequence-typed.
internal static class SoapEnvelopes
{
    public const string LdbNamespace = "http://thalesgroup.com/RTTI/2021-11-01/ldb/";
    public const string TokenNamespace = "http://thalesgroup.com/RTTI/2013-11-28/Token/types";
    public const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";

    // Build a GetDepartureBoardRequest envelope. filterCrs (toCrs) is optional;
    // when omitted Darwin returns all departures from the origin.
    public static string GetDepartureBoard(string token, string fromCrs, string? toCrs, int numRows)
    {
        // SecurityElement.Escape protects against accidental XML injection from
        // hostile CRS strings. CRS codes are 3-letter A-Z so this is belt-and-braces,
        // but cheap insurance and a habit worth keeping for any string interpolated
        // into an envelope.
        var safeToken = SecurityElement.Escape(token);
        var safeFrom = SecurityElement.Escape(fromCrs);
        var filter = string.IsNullOrWhiteSpace(toCrs)
            ? string.Empty
            : $"      <ldb:filterCrs>{SecurityElement.Escape(toCrs)}</ldb:filterCrs>\n";

        return $"""
        <soap:Envelope xmlns:soap="{SoapNamespace}"
                       xmlns:typ="{TokenNamespace}"
                       xmlns:ldb="{LdbNamespace}">
          <soap:Header>
            <typ:AccessToken>
              <typ:TokenValue>{safeToken}</typ:TokenValue>
            </typ:AccessToken>
          </soap:Header>
          <soap:Body>
            <ldb:GetDepartureBoardRequest>
              <ldb:numRows>{numRows}</ldb:numRows>
              <ldb:crs>{safeFrom}</ldb:crs>
        {filter}    </ldb:GetDepartureBoardRequest>
          </soap:Body>
        </soap:Envelope>
        """;
    }
}
