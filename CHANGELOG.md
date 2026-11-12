# Changelog

## Unreleased

### Builds without the MAUI workload (bam.maui#12)

Changed
- `UseMaui` and `SingleProject` are set only when the platform targets are built. A `net10.0`-only build (inside `bamtk.sln`, or with `BamPlatformTargets=false`) restores on an SDK with no workloads.
- `BamPlatformTargets=false` turns the platform targets off, as building inside `bamtk.sln` does. bamtest sets it for the projects it runs.

### Phase 2: native authenticators (bam.maui#4)

Added
- `AndroidPasskeyAuthenticator` (Credential Manager, Android 14+), `ApplePasskeyAuthenticator` (AuthenticationServices, iOS and Mac Catalyst 16+) and `WindowsPasskeyAuthenticator` (`webauthn.dll`).
- `PlatformPasskeyAuthenticator`, their shared base. It refuses options that are malformed or name a relying party other than the configured one before any platform is called.
- `WebAuthnCreationRequest` and `WebAuthnAssertionRequest`, the parsed options; `WebAuthnResponseJson` and `WebAuthnClientData`, which write standard WebAuthn JSON for Apple and Windows.
- `AndroidPasskeyErrors`, `ApplePasskeyErrors` and `WindowsPasskeyErrors`, which map each platform's failures onto `PasskeyErrorCategory`.
- `PasskeyErrorCategory.CredentialAlreadyRegistered` and its message, for a device that already holds a passkey from the exclude list.
- `PasskeyPlatform.CreateAuthenticator`. `AddPasskeyClient` binds its result when the host has not bound an authenticator.
- `bam.maui` and `bam.maui.tests` are in `bamtk.sln`.
- Platform setup and manual verification steps in the README.
- 17 unit tests (39 in all): suites `pkwj`, `pkwn`, `pkpe`, `pkpc`.

Changed
- Inside `bamtk.sln` the library builds `net10.0` only; `-p:BamMauiPlatformTargets` overrides.
- A cancelled operation's log line now names the exception type and its cause, so a real failure that lost a race with the caller's cancellation can still be found.
- `RelyingPartyId` refuses a last label that is `0x` plus hex digits; URL parsers read it as a number.
- The capability-probe test gives each probe its own client and a live token.

Review round 1
- Android: the caller's token ends a ceremony by itself (`PlatformCeremony`). Credential Manager does not have to call back once its signal is cancelled, so the ceremony no longer waits for a callback; a token that already fired never reaches Credential Manager.
- Android: errors are mapped by instance-of checks against the bound public exception and DOM error classes (`AndroidCredentialError`, `AndroidDomError`), not by Java class name. `getType()` was considered and rejected: androidx marks it `@RestrictTo(LIBRARY_GROUP)` and the binding flags it `XAOBS001`.
- Apple: the caller's token ends the wait the same way, and a token that fired before the sheet appeared shows no sheet.
- Options with a duplicate member are refused as `InvalidOptions`. Android forwards the raw JSON, and a provider that kept the first of two `rpId` members would act on a relying party the check never saw.
- An empty `pubKeyCredParams` means ES256 and RS256, as WebAuthn says, instead of being refused.
- Windows asks for no attestation when the options ask for `enterprise` or an unknown value, instead of leaving the choice to Windows.

Decisions
- A platform timeout is `ExpiredChallenge`, never `UserCancelled`.
- Android errors are mapped by `Java.Lang.Class.IsInstance` against the bound exception classes, because the binding passes the callback's error as a plain Java object and a C# type test does not compile. It uses only public API, and the classes it names are the ones the binding already needs at run time.
- The Windows authenticator states the origin `https://<rp id>`. Windows does not verify it.
- `Xamarin.AndroidX.Credentials` is pinned to 1.5.0.3, the newest version whose AndroidX dependencies agree with MAUI 10.0.20.

Outstanding
- No ceremony has been run on a device. The README lists the steps.
- Apple: `excludeCredentials` is not passed (the binding does not expose it for platform passkeys) and security keys are not offered.
- Android below API 34 (Phase 6).
- HTTP backend and views (phases 3 to 5).

### Phase 1: passkey foundation (bam.maui#2)

Added
- `Bam.Maui.Passkeys`: `IPasskeyClient`, `IPasskeyAuthenticator`, `IPasskeyBackend` and the orchestrating `PasskeyClient`.
- Models: WebAuthn JSON wrappers, registration request, challenges, `PasskeySignIn`, `PasskeyCredential`, `PasskeyCapabilities`, `PasskeyStatus`, `PasskeyResult`.
- Errors: `PasskeyErrorCategory`, `PasskeyError`, `PasskeyException`, `PasskeyErrorNormalizer`.
- Configuration: `PasskeyConfiguration`, `PasskeyFeatures`, `PasskeyBranding`, `PasskeyMessages`, each with validation.
- `UnsupportedPasskeyAuthenticator`, the default until a platform authenticator is registered.
- Registration: `ServiceRegistry.AddPasskeyClient` and `IServiceCollection.AddBamPasskeys`.
- `bam.maui.tests` (bam.test): 22 unit tests.

Changed
- Minimum iOS and Mac Catalyst versions raised to 15.0, the minimum MAUI 10 supports.
- Platform targets are skipped on Linux.

Removed
- The scaffold `Class1` and `PlatformClass1` files, and the unused `Microsoft.Maui.Controls.Compatibility` reference.

Review round 1
- A cancellation is `UserCancelled` only when the caller cancelled or the authenticator reported it; a backend cancellation with the caller's token live is `NetworkFailure`.
- A capability probe cancelled by the caller rethrows instead of reporting "not supported".
- `RelyingPartyId` must be a lowercase DNS host name; colours are matched to the end of the string.
- `PasskeyRegistrationRequest` and `PasskeyAuthenticationChallenge` print only their type name.
- Log content is pinned by tests; a logger that throws can't break a result; the inner exception's type name is logged.
- Needs bam.base#42 (`Logger.BlockUntilEventQueueIsEmpty`) for the log tests.

Review round 2
- A probe the caller cancelled is cancelled whatever the authenticator threw; the token decides, not the exception type.
- Once the caller cancels, the operation reports `UserCancelled` even when the failure looks like a timeout.
- A stray cancellation from a probe is logged as a failed probe.
- `RelyingPartyId` refuses IP addresses and all-digit names.
- A plain `net10.0` target, and a `bam.base` reference resolved through `$(BamFrameworkRoot)`.

Decisions
- Platform integrations are the library's own, behind `IPasskeyAuthenticator`. MAUI's Essentials passkey API needs .NET 11; bamtk is on .NET 10.
- WebAuthn data is carried as standard JSON, not parsed into typed fields, so the backend's HTTP contract is the only contract and the app doesn't reference bam.useraccounts.
- Colours in `PasskeyBranding` are hex strings so configuration has no UI dependency.

Outstanding at the end of Phase 1
- No platform authenticator, HTTP backend or views (phases 2 to 5).
- `bam.maui` was not in `bamtk.sln`.
