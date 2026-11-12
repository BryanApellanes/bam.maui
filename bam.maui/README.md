# bam.maui

MAUI building blocks for the Bam toolkit. Today that is one thing: `Bam.Maui.Passkeys`, a white-label passkey client for .NET MAUI apps.

## Bam.Maui.Passkeys

Apps use it to register an account with a passkey, sign in (with or without a username) and manage passkeys. Three parties are involved and each owns one thing:

- The operating system owns the ceremony: the biometric prompt, the credential picker, the key. These prompts can't be branded or re-skinned, and the library doesn't try.
- The backend owns verification: it issues every challenge and decides whether a ceremony succeeded. The app never verifies a signature.
- The library owns what's in between: orchestration, error handling, configuration, and (from Phase 4) the branded screens around the prompts.

### Status

Phases 1 and 2 of 7 are in place. Tracking issue: BryanApellanes/bam.maui#1.

| Phase | What | State |
|---|---|---|
| 1 | Contracts, configuration, orchestration, DI, unit tests | Done |
| 2 | Native authenticators (Android, iOS, Mac Catalyst, Windows) | Done. Compiled and unit tested; no ceremony has been run on a device yet (see [Manual verification](#manual-verification)) |
| 3 | HTTP backend for bam.useraccounts.web | Not started |
| 4 | White-label views and theming | Not started |
| 5 | Passkey management UI | Not started |
| 6 | Older Android, platform matrix | Not started |
| 7 | Hardening, docs, sample app | Not started |

The library never reports support it can't deliver: `IsSupportedAsync` is true only when the platform's authenticator says a ceremony can run. There is no backend yet (Phase 3), so an app binds its own `IPasskeyBackend` for now.

### Platform support

| Platform | Native API | Authenticator | Notes |
|---|---|---|---|
| Android 14+ (API 34) | AndroidX Credential Manager | `AndroidPasskeyAuthenticator` | Older Android reports `UnsupportedOperatingSystem` until Phase 6 |
| iOS 16+ | AuthenticationServices | `ApplePasskeyAuthenticator` | Device passkeys only; no security keys yet |
| Mac Catalyst 16+ | AuthenticationServices | `ApplePasskeyAuthenticator` | Same as iOS |
| Windows 10 1903+ | `webauthn.dll` | `WindowsPasskeyAuthenticator` | Windows Hello, security keys and phones. The library loads on 1809, the MAUI minimum, and reports passkeys as not supported there |
| Tizen | None | `UnsupportedPasskeyAuthenticator` | Not targeted |

MAUI's own passkey API in Essentials arrives with .NET 11. bamtk is on .NET 10 / MAUI 10.0.20, which doesn't have it, so the platform integrations are the library's own behind `IPasskeyAuthenticator`. An Essentials-backed authenticator can be added as one more implementation later.

What each platform can and can't tell the app before a ceremony:

| | Android | iOS / Mac Catalyst | Windows |
|---|---|---|---|
| OS too old | Yes | Yes | Yes (no `webauthn.dll`) |
| No screen lock or passcode | Yes | Yes | Not asked; a security key works without one |
| No credential provider | Only when a ceremony fails (`MissingCredentialProvider`) | No; the system tells the user itself | Not applicable |
| Security keys | Yes | Not in this phase | Yes |

### Architecture

```
App ──> IPasskeyClient (PasskeyClient)
            │  1. begin      ┌──> IPasskeyBackend        (relying-party server: challenges, verification, session)
            │  2. ceremony   └──> IPasskeyAuthenticator  (OS passkey API for the platform)
            │  3. complete   ───> IPasskeyBackend
            └─ failures ───> PasskeyErrorNormalizer ──> PasskeyError (category + configured message)
```

WebAuthn data travels as standard WebAuthn JSON in four small wrappers (`WebAuthnCreationOptions`, `WebAuthnRequestOptions`, `WebAuthnRegistrationResponse`, `WebAuthnAssertionResponse`). `PasskeyClient` passes them through without reading them, so the backend's HTTP contract is the only contract.

The platform authenticators all derive from `PlatformPasskeyAuthenticator`. It reads the options once, refuses them if they are malformed or name a relying party other than the configured `RelyingPartyId`, and only then calls the platform. What happens next differs:

- Android hands the options JSON to Credential Manager and returns the response JSON it gets back.
- Apple takes the challenge, user and allow list from the parsed options and builds the response JSON from the raw result.
- Windows does the same and also writes `clientDataJSON` itself, because `webauthn.dll` leaves that to the caller. The origin it writes is `https://` plus the relying party id.

The contracts, the orchestration and everything that doesn't need a device live in the plain `net10.0` target. Each platform target (`net10.0-android`, `-ios`, `-maccatalyst`, `-windows`) adds its own authenticator. The Windows authenticator is plain P/Invoke, so it also compiles for `net10.0`, which is where its tests run. On Linux only `net10.0` is built, because the Apple and Android workloads don't restore there.

Inside `bamtk.sln` the library builds `net10.0` only. `run-tests.sh` builds that solution into one output folder, and several targets of one project would overwrite each other there. Building the project directly, or from an app's solution, builds every target. `-p:BamMauiPlatformTargets=true` or `false` overrides either way.

### Using it

```csharp
ServiceRegistry registry = new ServiceRegistry();
registry.For<IPasskeyBackend>().Use(new MyBackend());              // your relying-party client
// The platform's authenticator is bound for you. Bind your own IPasskeyAuthenticator here to replace it.

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

`PasskeyErrorCategory` is the one vocabulary on every platform: `UserCancelled`, `NoCredentials`, `UnsupportedOperatingSystem`, `MissingCredentialProvider`, `DeviceSecurityNotConfigured`, `NetworkFailure`, `InvalidServerResponse`, `InvalidOptions`, `ExpiredChallenge`, `ServerVerificationFailed`, `PlatformFailure`, `FeatureDisabled`, `CredentialAlreadyRegistered`, `Unknown`.

Cancellation is not a failure; check `WasCancelled` and return the user to where they were. The message on a `PasskeyError` always comes from `PasskeyMessages`, never from an exception. Replace its members to reword or localize.

Authenticators and backends report a known failure by throwing `PasskeyException(category)`. `HttpRequestException` and timeouts become `NetworkFailure` and malformed JSON becomes `InvalidServerResponse`.

A cancellation is `UserCancelled` only when the caller's token was cancelled or the authenticator reported it. A cancellation thrown by the backend while the caller's token is live (an aborted request, the backend's own timeout) is `NetworkFailure`, so a network fault never looks like the user's choice. Once the caller's token is cancelled, any failure of that operation is reported as cancelled, whatever the platform threw.

How the platforms' own failures map:

| Category | Android | Apple | Windows |
|---|---|---|---|
| `UserCancelled` | `CreateCredentialCancellationException`, `GetCredentialCancellationException` | `ASAuthorizationError` 1001 | `NTE_USER_CANCELLED`, `ERROR_CANCELLED` |
| `NoCredentials` | `NoCredentialException` | Not reported; the system shows its own sheet | `NTE_NOT_FOUND`, `NTE_DEVICE_NOT_FOUND` when signing in |
| `CredentialAlreadyRegistered` | DOM `InvalidStateError` when creating | `ASAuthorizationError` 1006 | `NTE_EXISTS` when creating |
| `ExpiredChallenge` | DOM `TimeoutError` | | `ERROR_TIMEOUT` |
| `MissingCredentialProvider` | Provider configuration and no-create-option exceptions | | |
| `InvalidOptions` | DOM `DataError`, `ConstraintError`, `NotSupportedError`, `SyntaxError`, `EncodingError` | | `NTE_INVALID_PARAMETER` |
| `PlatformFailure` | Everything else, including DOM `NotAllowedError` and `SecurityError` | Everything else, including an app not associated with the domain | Everything else |

Only a definite dismissal is `UserCancelled`. Android providers use `NotAllowedError` for failures the user didn't choose, so it isn't treated as one.

### Platform setup

The relying party id in `PasskeyConfiguration` must be the domain the backend uses as its RP id. Each platform then needs the domain to vouch for the app.

**Android**

1. Serve `https://<rp id>/.well-known/assetlinks.json` naming the app's package and the SHA-256 fingerprint of its signing certificate, with the relations `delegate_permission/common.handle_all_urls` and `delegate_permission/common.get_login_creds`.
2. The backend must allow the app's origin, `android:apk-key-hash:` plus the unpadded base64url SHA-256 of the signing certificate. In bam.useraccounts that is `PasskeyOptions.AllowedAppOrigins`.
3. Debug and release builds are signed with different certificates, so each needs its own fingerprint and origin.
4. The device needs a screen lock and a credential provider (Google Password Manager or another) with passkeys enabled.

**iOS and Mac Catalyst**

1. Add the Associated Domains entitlement with `webcredentials:<rp id>` to `Entitlements.plist`.
2. Serve `https://<rp id>/.well-known/apple-app-site-association` with a `webcredentials` section listing `<Team ID>.<bundle id>`. Apple fetches it through its CDN, so a change can take a while to show.
3. The device needs a passcode, and iCloud Keychain or another passkey provider turned on.
4. The origin the backend sees is `https://<rp id>`, so nothing extra is needed there.

**Windows**

1. No manifest entry or association file is needed.
2. The origin the backend sees is `https://<rp id>`.
3. Windows does not check that a desktop app belongs to the relying party it names. The check that the options are for the configured relying party is the library's.

Serving the two association files from bam.useraccounts is tracked in BryanApellanes/bam.useraccounts#13.

### Manual verification

No ceremony has been run on a device. Nothing in the development environment can run one: there is no Android device or emulator session, no Mac, and a Windows ceremony needs a person at the Windows Hello prompt. What has been checked: every platform target compiles against the real bindings, the interop structures match Microsoft's `webauthn.h` (measured with a C compiler), and the real `webauthn.dll` answers the two read-only probes.

Run these on each platform against a backend whose RP id matches the app's configuration, and record the result on BryanApellanes/bam.maui#1:

1. `IsSupportedAsync` is true on a set-up device.
2. Remove the screen lock (Android) or passcode (iOS): `GetCapabilitiesAsync` reports `DeviceSecurityNotConfigured`.
3. `RegisterPasskeyAsync` shows the system prompt and the backend accepts the registration.
4. `AuthenticateWithUsernameAsync` signs in with that passkey.
5. `AuthenticateAsync` (no username) offers the passkey and signs in.
6. Dismiss the prompt: the result is `UserCancelled` and `WasCancelled` is true.
7. Cancel the token while the prompt is up: the result is `UserCancelled` at once, and the prompt closes. On Android the result does not wait for Credential Manager to call back.
8. `AddPasskeyAsync` on a device that already holds a passkey for the account: `CredentialAlreadyRegistered` on Android and Windows. On Apple the backend refuses the duplicate instead, because the exclude list isn't passed there yet.
9. Sign in with a username that has no passkey on the device: `NoCredentials` on Android and Windows.
10. Point the app at options for another RP id: `InvalidOptions`, and no system prompt appears.
11. Android only: break `assetlinks.json` and confirm the ceremony fails rather than succeeding.
12. iOS only: remove the associated domain and confirm the ceremony fails with `PlatformFailure`.

Known unknown these steps settle: whether the Apple sheet needs anything beyond the current view controller's window as its anchor. (Android errors are mapped by Credential Manager's type strings, which survive R8 renaming the exception classes; the Java class name is only a fallback.)

### Branding

`PasskeyBranding` holds the application name, logo, colours (hex strings), dark-scheme colours, corner radius, font family and theme mode. It's validated at registration and injectable. The views that read it arrive in Phase 4; nothing in the library hardcodes a brand.

### Security

- The app verifies nothing. A ceremony has succeeded only when the backend's complete call returns, and the signed-in identity is taken from the backend's answer, never from what the user typed. A completion that names no account is treated as a failure.
- Challenges are generated, expired and consumed by the backend. The library holds one only for the length of a ceremony.
- Private keys and biometric data never reach the app; the platform APIs don't expose them.
- Nothing sensitive is logged. Failures are logged by operation, category and exception type names. Exception messages and WebAuthn payloads are not. The payload wrappers, the registration request (email, invite code) and the authentication challenge (ceremony id) print only their type name. Tests pin all of this.
- `PasskeyFeatures` switches decide what the app offers. They are not access control; the backend enforces what an account may do.
- `PasskeySignIn.NextUrl` is whatever the backend sent. Check it is a relative path or on the relying party's origin before navigating to it.
- Every platform authenticator refuses options whose `rp.id` (or `rpId`) is missing or is not the configured `RelyingPartyId`, before the platform is called. Options for another relying party never reach a prompt. Options with a duplicate member are refused too, because Android hands the raw JSON to Credential Manager and a provider's reader may keep a different copy of `rpId` than the check saw.
- Refusals never quote the options. A malformed challenge or a foreign RP id is named by member, not by value.
- Native apps need the relying-party domain to vouch for them: Digital Asset Links for Android, Associated Domains for Apple. See [Platform setup](#platform-setup).
- On Windows the app states its own origin and the OS doesn't verify it. A Windows passkey ceremony proves possession of the credential for the RP id; it doesn't prove which app asked.

### Tests

`bam.maui.tests` uses bam.test with hand-written fakes for the backend, the authenticator and `webauthn.dll`. No test needs biometric hardware or shows a prompt. The projects reference the framework through `$(BamFrameworkRoot)`, so build them with `bam.maui` checked out inside bamtk (`submodules/bam.maui`).

```bash
dotnet build bam.maui.tests/bam.maui.tests.csproj -c Release
BAM_ARG_STYLE=Posix dotnet run --project bam.maui.tests/bam.maui.tests.csproj -c Release --no-build -- --ut
```

Suites: `pkcf` configuration, branding, capabilities and what the models print; `pken` error normalization; `pkcl` client orchestration, cancellation, feature switches and management; `pkdg` where a cancellation came from, and what reaches the log; `pkrg` registration; `pkwj` reading options and writing response JSON; `pkwn` the Windows authenticator and its interop layout; `pkpe` the Android, Apple and Windows error maps; `pkpc` a callback ceremony ending from the caller's token.

### Dependencies

- `Microsoft.Maui.Controls` 10.0.20
- `Xamarin.AndroidX.Credentials` 1.5.0.3, Android target only. It is Microsoft's binding of `androidx.credentials`; bamtk has nothing that does this. 1.5.0.3 is the newest version whose AndroidX dependencies agree with MAUI 10.0.20
- `bam.base` (`ServiceRegistry`, `Bam.Logging.ILogger`), which brings `Newtonsoft.Json`; the error normalizer recognises its exceptions
