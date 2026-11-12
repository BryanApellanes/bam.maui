using System.Net;
using System.Net.Http;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Keeps the account session cookie for <see cref="HttpPasskeyBackend"/>.  It sits in front of the
/// platform's handler (which runs with cookies off) so the session behaves the same on every
/// platform, whatever that handler does with cookies.  <see cref="CookieContainer"/> applies the
/// cookie rules: <c>Secure</c> cookies only over https, domain and path scoping, expiry.
/// </summary>
internal sealed class PasskeySessionCookieJar : DelegatingHandler
{
    private const string CookieHeader = "Cookie";
    private const string SetCookieHeader = "Set-Cookie";

    private CookieContainer _cookies = new CookieContainer();

    internal PasskeySessionCookieJar(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    /// <summary>The cookies held, for tests.</summary>
    internal CookieContainer Cookies => Volatile.Read(ref _cookies);

    /// <summary>
    /// Forgets every cookie: the session has ended.  A response already in flight writes into the old container, which
    /// nothing reads again.
    /// </summary>
    internal void Clear()
    {
        Volatile.Write(ref _cookies, new CookieContainer());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri ?? throw new InvalidOperationException("The request has no URI.");
        request.Headers.Remove(CookieHeader);
        CookieContainer cookies = Cookies;
        string cookieHeader = cookies.GetCookieHeader(uri);
        if (cookieHeader.Length > 0)
        {
            request.Headers.TryAddWithoutValidation(CookieHeader, cookieHeader);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Headers.TryGetValues(SetCookieHeader, out IEnumerable<string>? values))
        {
            foreach (string value in values)
            {
                try
                {
                    cookies.SetCookies(uri, value);
                }
                catch (CookieException)
                {
                    // A malformed cookie is dropped; the endpoints then answer 401 and the app signs in again.
                }
            }
        }
        return response;
    }
}
