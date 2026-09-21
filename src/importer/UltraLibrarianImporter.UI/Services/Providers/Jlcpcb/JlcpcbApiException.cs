using System;

namespace UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

/// <summary>
/// A JLCPCB source answered, but not with parts: an error status, a business error code in the
/// body, or a body without the expected data. It is a failure, never "no results", so the
/// aggregator leaves the provider out and does not cache it. The message carries what JLCPCB said
/// and, for the official API, the request's <c>J-Trace-ID</c>, which JLCPCB's documentation asks for
/// when reporting a problem. It never carries a credential.
/// </summary>
public sealed class JlcpcbApiException : Exception
{
    public JlcpcbApiException(string message)
        : base(message)
    {
    }
}
