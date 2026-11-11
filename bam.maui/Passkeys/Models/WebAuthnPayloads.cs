namespace Bam.Maui.Passkeys;

/// <summary>
/// Standard WebAuthn <c>PublicKeyCredentialCreationOptionsJSON</c>, exactly as the relying party
/// issued it.  The library carries it from the backend to the authenticator without reading it.
/// </summary>
public sealed record WebAuthnCreationOptions
{
    /// <summary>Wraps creation options JSON.</summary>
    /// <param name="json">The options JSON; must not be blank.</param>
    /// <exception cref="ArgumentException">The JSON is null or blank.</exception>
    public WebAuthnCreationOptions(string json)
    {
        Json = WebAuthnPayload.Require(json, nameof(WebAuthnCreationOptions));
    }

    /// <summary>The options JSON.</summary>
    public string Json { get; }

    /// <summary>Names the type only, so a payload never lands in a log through string formatting.</summary>
    public override string ToString() => nameof(WebAuthnCreationOptions);
}

/// <summary>
/// Standard WebAuthn <c>PublicKeyCredentialRequestOptionsJSON</c>, exactly as the relying party
/// issued it.
/// </summary>
public sealed record WebAuthnRequestOptions
{
    /// <summary>Wraps request options JSON.</summary>
    /// <param name="json">The options JSON; must not be blank.</param>
    /// <exception cref="ArgumentException">The JSON is null or blank.</exception>
    public WebAuthnRequestOptions(string json)
    {
        Json = WebAuthnPayload.Require(json, nameof(WebAuthnRequestOptions));
    }

    /// <summary>The options JSON.</summary>
    public string Json { get; }

    /// <summary>Names the type only, so a payload never lands in a log through string formatting.</summary>
    public override string ToString() => nameof(WebAuthnRequestOptions);
}

/// <summary>
/// Standard WebAuthn <c>RegistrationResponseJSON</c>: what the authenticator produced when it
/// created a credential.  Sent to the backend unchanged.
/// </summary>
public sealed record WebAuthnRegistrationResponse
{
    /// <summary>Wraps a registration response.</summary>
    /// <param name="json">The response JSON; must not be blank.</param>
    /// <exception cref="ArgumentException">The JSON is null or blank.</exception>
    public WebAuthnRegistrationResponse(string json)
    {
        Json = WebAuthnPayload.Require(json, nameof(WebAuthnRegistrationResponse));
    }

    /// <summary>The response JSON.</summary>
    public string Json { get; }

    /// <summary>Names the type only, so a payload never lands in a log through string formatting.</summary>
    public override string ToString() => nameof(WebAuthnRegistrationResponse);
}

/// <summary>
/// Standard WebAuthn <c>AuthenticationResponseJSON</c>: the signed assertion the authenticator
/// produced.  Sent to the backend unchanged.
/// </summary>
public sealed record WebAuthnAssertionResponse
{
    /// <summary>Wraps an assertion response.</summary>
    /// <param name="json">The response JSON; must not be blank.</param>
    /// <exception cref="ArgumentException">The JSON is null or blank.</exception>
    public WebAuthnAssertionResponse(string json)
    {
        Json = WebAuthnPayload.Require(json, nameof(WebAuthnAssertionResponse));
    }

    /// <summary>The response JSON.</summary>
    public string Json { get; }

    /// <summary>Names the type only, so a payload never lands in a log through string formatting.</summary>
    public override string ToString() => nameof(WebAuthnAssertionResponse);
}

internal static class WebAuthnPayload
{
    internal static string Require(string json, string typeName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException($"{typeName} requires non-empty JSON.", nameof(json));
        }
        return json;
    }
}
