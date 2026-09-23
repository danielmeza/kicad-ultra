using System;

namespace UltraLibrarianImporter.UI.Services.Providers.Jlcpcb;

/// <summary>
/// A JLCPCB source answered, but not with parts: an error status, a business error code in the
/// body, or a body without the expected data. It is a failure, never "no results", so the
/// aggregator leaves the provider out and does not cache it. The message carries what JLCPCB said
/// and, for the official API, the request's <c>J-Trace-ID</c>, which JLCPCB's documentation asks for
/// when reporting a problem. It never carries a credential.
/// </summary>
public class JlcpcbApiException : Exception
{
    public JlcpcbApiException(string message)
        : base(message)
    {
    }

    public JlcpcbApiException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Why JLCPCB will not let this application call the official Components API (#126).</summary>
public enum JlcpcbApiRefusal
{
    /// <summary>
    /// The platform refused the call itself: JLCPCB grants API access per service, and the
    /// Components API needs the application's <b>Parts</b> permission approved. An application's IP
    /// whitelist can block a call the same way.
    /// </summary>
    NotApproved,

    /// <summary>
    /// JLCPCB did not accept the credentials: an App ID or Access Key it does not know, a secret key
    /// that signs differently from the one it holds, or a value that cannot be sent at all.
    /// </summary>
    CredentialsRejected,
}

/// <summary>
/// JLCPCB refused this application the official Components API, rather than failing to answer one
/// lookup (#126). The credentials are the user's own, so no retry can help: the lookup is answered
/// from the website endpoint instead, and <see cref="JlcpcbOfficialApiAccess"/> remembers the
/// refusal so that the rest of the session does not call the API again.
/// </summary>
/// <remarks>
/// Only what JLCPCB documents as such becomes one of these. Its "Error Information" section
/// (https://api.jlcpcb.com/docs/start) documents HTTP 401 as "Unauthorized request. Usually due to
/// signature verification failure" and HTTP 403 as "Forbidden. The request is not allowed"; those
/// are <see cref="JlcpcbApiRefusal.CredentialsRejected"/> and
/// <see cref="JlcpcbApiRefusal.NotApproved"/>. Everything else — 400, 500, any other status, and
/// every business <c>code</c> in the body — is still a plain failure that throws, because JLCPCB
/// publishes no error code that means "this application is not approved for this interface".
/// </remarks>
public sealed class JlcpcbApiAccessDeniedException : JlcpcbApiException
{
    public JlcpcbApiAccessDeniedException(JlcpcbApiRefusal refusal, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Refusal = refusal;
    }

    /// <summary>What JLCPCB's refusal means, as far as it can be told apart.</summary>
    public JlcpcbApiRefusal Refusal { get; }
}
