# bam.maui

MAUI building blocks for the Bam toolkit. Today that is one thing: `Bam.Maui.Passkeys`, a white-label passkey client for .NET MAUI apps.

## Bam.Maui.Passkeys

Apps use it to register an account with a passkey, sign in (with or without a username) and manage passkeys. Three parties are involved and each owns one thing:

- The operating system owns the ceremony: the biometric prompt, the credential picker, the key. These prompts can't be branded or re-skinned, and the library doesn't try.
- The backend owns verification: it issues every challenge and decides whether a ceremony succeeded. The app never verifies a signature.
- The library owns what's in between: orchestration, error handling, configuration, and (from Phase 4) the branded screens around the prompts.

### Status

Phase 1 of 7 is in place. Tracking issue: BryanApellanes/bam.maui#1.

| Phase | What | State |
|---|---|---|
| 1 | Contracts, configuration, orchestration, DI, unit tests | Done |
| 2 | Native authenticators (Android, iOS, Mac Catalyst, Windows) | Not started |
| 3 | HTTP backend for bam.useraccounts.web | Not started |
| 4 | White-label views and theming | Not started |
| 5 | Passkey management UI | Not started |
| 6 | Older Android, platform matrix | Not started |
| 7 | Hardening, docs, sample app | Not started |

Until Phase 2 no platform authenticator ships, so `IsSupportedAsync` returns false everywhere and ceremonies fail with `UnsupportedOperatingSystem`. That's deliberate: the library never reports support it can't deliver. A host can bind its own `IPasskeyAuthenticator` today.

### Platform support

| Platform | Native API (planned) | State |
|---|---|---|
| Android | AndroidX Credential Manager | Phase 2 |
| iOS 15+ | AuthenticationServices | Phase 2 |
| Mac Catalyst 15+ | AuthenticationServices | Phase 2 |
| Windows 10 1903+ | `webauthn.dll` | Phase 2. The library loads on 1809, the MAUI minimum, and will report passkeys as not supported there |
| Tizen | None | Not targeted; reports not supported |

MAUI's own passkey API in Essentials arrives with .NET 11. bamtk is on .NET 10 / MAUI 10.0.20, which doesn't have it, so the platform integrations sit behind `IPasskeyAuthenticator`. An Essentials-backed authenticator can be added as one more implementation later.

### Architecture

```
App ──> IPasskeyClient (PasskeyClient)
            │  1. begin      ┌──> IPasskeyBackend        (relying-party server: challenges, verification, session)
            │  2. ceremony   └──> IPasskeyAuthenticator  (OS passkey API for the platform)
            │  3. complete   ───> IPasskeyBackend
            └─ failures ───> PasskeyErrorNormalizer ──> PasskeyError (category + configured message)
```

WebAuthn data travels as standard WebAuthn JSON in four small wrappers (`WebAuthnCreationOptions`, `WebAuthnRequestOptions`, `WebAuthnRegistrationResponse`, `WebAuthnAssertionResponse`). The service passes them through without reading them, so the backend's HTTP contract is the only contract.

Everything lives in the plain `net10.0` target, so it builds and tests without a device. The platform targets (`net10.0-android`, `-ios`, `-maccatalyst`, `-windows`) compile the same code and gain their authenticators in Phase 2. On Linux only `net10.0` is built, because the Apple and Android workloads don't restore there.

### Using it

```csharp
ServiceRegistry registry = new ServiceRegistry();
registry.For<IPasskeyBackend>().Use(new MyBackend());              // your relying-party client
// registry.For<IPasskeyAuthenticator>().Use(...);                 // a platform authenticator, from Phase 2

PasskeyConfiguration configuration = new PasskeyConfiguration
{
    RelyingPartyId = "example.com",
    Branding = new PasskeyBranding
    {
        ApplicationName = "Example Application",
        Logo = "brand_logo.png",
        PrimaryColor = "#2457C5",
        CornerRadius = 12
    },
    Features = new PasskeyFeatures { EnableManagement = true }
};

builder.Services.AddBamPasskeys(registry, configuration);          // in MauiProgram
```

Then inject `IPasskeyClient`:

```csharp
PasskeyResult<PasskeySignIn> result = await passkeys.AuthenticateAsync(cancellationToken);
if (result.Succeeded)
{
    // result.Value.Handle is who the backend says signed in
}
else if (!result.WasCancelled)
{
    // result.Error.Category to branch on, result.Error.Message to show
}
```

`AddPasskeyClient(this ServiceRegistry, PasskeyConfiguration)` does the same without MAUI's service collection.

### API

| Member | Does |
|---|---|
| `IsSupportedAsync`, `GetCapabilitiesAsync` | Ask the device, without prompting |
| `RegisterPasskeyAsync(request)` | Create an account with its first passkey |
| `AddPasskeyAsync()` | Add a passkey to the signed-in account |
| `AuthenticateAsync()` | Username-less sign-in |
| `AuthenticateWithUsernameAsync(handle)` | Username-first sign-in |
| `GetRegisteredPasskeysAsync()`, `DeletePasskeyAsync(id)` | Manage the signed-in account's passkeys |
| `GetPasskeyStatusAsync()` | Capabilities plus the passkey count when it can be read |

No ceremony or management method throws for an expected failure. Each returns a `PasskeyResult`. The three probes (`IsSupportedAsync`, `GetCapabilitiesAsync`, `GetPasskeyStatusAsync`) throw `OperationCanceledException` when the caller cancels, and nothing else: a cancelled probe says nothing about the device, so it is never reported as "not supported".

### Errors

`PasskeyErrorCategory` is the one vocabulary on every platform: `UserCancelled`, `NoCredentials`, `UnsupportedOperatingSystem`, `MissingCredentialProvider`, `DeviceSecurityNotConfigured`, `NetworkFailure`, `InvalidServerResponse`, `InvalidOptions`, `ExpiredChallenge`, `ServerVerificationFailed`, `PlatformFailure`, `FeatureDisabled`, `Unknown`.

Cancellation is not a failure; check `WasCancelled` and return the user to where they were. The message on a `PasskeyError` always comes from `PasskeyMessages`, never from an exception. Replace its members to reword or localize.

Authenticators and backends report a known failure by throwing `PasskeyException(category)`. `HttpRequestException` and timeouts become `NetworkFailure` and malformed JSON becomes `InvalidServerResponse`.

A cancellation is `UserCancelled` only when the caller's token was cancelled or the authenticator reported it. A cancellation thrown by the backend while the caller's token is live (an aborted request, the backend's own timeout) is `NetworkFailure`, so a network fault never looks like the user's choice. Once the caller's token is cancelled, any failure of that operation is reported as cancelled, whatever the platform threw.

### Branding

`PasskeyBranding` holds the application name, logo, colours (hex strings), dark-scheme colours, corner radius, font family and theme mode. It's validated at registration and injectable. The views that read it arrive in Phase 4; nothing in the library hardcodes a brand.

### Security

- The app verifies nothing. A ceremony has succeeded only when the backend's complete call returns, and the signed-in identity is taken from the backend's answer, never from what the user typed. A completion that names no account is treated as a failure.
- Challenges are generated, expired and consumed by the backend. The library holds one only for the length of a ceremony.
- Private keys and biometric data never reach the app; the platform APIs don't expose them.
- Nothing sensitive is logged. Failures are logged by operation, category and exception type names. Exception messages and WebAuthn payloads are not. The payload wrappers, the registration request (email, invite code) and the authentication challenge (ceremony id) print only their type name. Tests pin all of this.
- `PasskeyFeatures` switches decide what the app offers. They are not access control; the backend enforces what an account may do.
- `PasskeySignIn.NextUrl` is whatever the backend sent. Check it is a relative path or on the relying party's origin before navigating to it.
- Native apps need the relying-party domain to vouch for them: Digital Asset Links for Android, Associated Domains for Apple. Android also presents an `android:apk-key-hash:` origin the server must allow. Server-side work for both is tracked in BryanApellanes/bam.useraccounts#12 and #13; the per-platform setup steps land with Phase 2.

### Tests

`bam.maui.tests` uses bam.test with hand-written fakes for the backend and the authenticator. No test needs biometric hardware. The projects reference the framework through `$(BamFrameworkRoot)`, so build them with `bam.maui` checked out inside bamtk (`submodules/bam.maui`).

```bash
dotnet build bam.maui.tests/bam.maui.tests.csproj -c Release
BAM_ARG_STYLE=Posix dotnet run --project bam.maui.tests/bam.maui.tests.csproj -c Release --no-build -- --ut
```

Suites: `pkcf` configuration, branding, capabilities and what the models print; `pken` error normalization; `pkcl` client orchestration, cancellation, feature switches and management; `pkdg` where a cancellation came from, and what reaches the log; `pkrg` registration.

### Dependencies

- `Microsoft.Maui.Controls` 10.0.20
- `bam.base` (`ServiceRegistry`, `Bam.Logging.ILogger`), which brings `Newtonsoft.Json`; the error normalizer recognises its exceptions
