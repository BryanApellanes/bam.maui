namespace Bam.Maui.Passkeys;

/// <summary>
/// Where and how <see cref="HttpPasskeyBackend"/> reaches the bam account endpoints.
/// </summary>
public sealed class PasskeyHttpBackendOptions
{
    /// <summary>The default request timeout.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The default cap on a response body, in bytes.</summary>
    public const int DefaultMaxResponseBytes = 256 * 1024;

    /// <summary>
    /// The base of the account endpoints, e.g. <c>https://example.com/account/</c>.  Null means
    /// <c>https://&lt;relying party id&gt;/account/</c>.  It must be https, carry no user info, query or
    /// fragment, and its host must be the relying party id or a subdomain of it, so the session
    /// cookie and the ceremonies only ever go to the relying party.
    /// </summary>
    public Uri? AccountEndpoint { get; set; }

    /// <summary>How long one request may take.  Must be positive.</summary>
    public TimeSpan Timeout { get; set; } = DefaultTimeout;

    /// <summary>The largest response body accepted, in bytes; a larger one is an invalid response.  Must be positive.</summary>
    public int MaxResponseBytes { get; set; } = DefaultMaxResponseBytes;

    /// <summary>Checks the options against the configured relying party and returns the endpoint base, ending in <c>/</c>.</summary>
    /// <param name="relyingPartyId">The validated relying party id.</param>
    /// <returns>The account endpoint base.</returns>
    /// <exception cref="ArgumentException">An option is invalid; the message names it.</exception>
    internal Uri Resolve(string relyingPartyId)
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("PasskeyHttpBackendOptions.Timeout must be positive.", nameof(Timeout));
        }
        if (MaxResponseBytes <= 0)
        {
            throw new ArgumentException("PasskeyHttpBackendOptions.MaxResponseBytes must be positive.", nameof(MaxResponseBytes));
        }

        Uri endpoint = AccountEndpoint ?? new Uri("https://" + relyingPartyId + "/account/");
        if (!endpoint.IsAbsoluteUri || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("PasskeyHttpBackendOptions.AccountEndpoint must be an absolute https URI.", nameof(AccountEndpoint));
        }
        if (endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
        {
            throw new ArgumentException("PasskeyHttpBackendOptions.AccountEndpoint must not carry user info, a query or a fragment.", nameof(AccountEndpoint));
        }
        string host = endpoint.IdnHost;
        bool sameSite = string.Equals(host, relyingPartyId, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + relyingPartyId, StringComparison.OrdinalIgnoreCase);
        if (!sameSite)
        {
            throw new ArgumentException("PasskeyHttpBackendOptions.AccountEndpoint must be on the relying party id or a subdomain of it.", nameof(AccountEndpoint));
        }

        string text = endpoint.GetLeftPart(UriPartial.Path);
        return text.EndsWith('/') ? new Uri(text) : new Uri(text + "/");
    }
}
