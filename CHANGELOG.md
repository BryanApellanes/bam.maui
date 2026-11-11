# Changelog

## Unreleased

### Phase 1: passkey foundation (bam.maui#2)

Added
- `Bam.Maui.Passkeys`: `IPasskeyClient`, `IPasskeyAuthenticator`, `IPasskeyBackend` and the orchestrating `PasskeyClient`.
- Models: WebAuthn JSON wrappers, registration request, challenges, `PasskeySignIn`, `PasskeyCredential`, `PasskeyCapabilities`, `PasskeyStatus`, `PasskeyResult`.
- Errors: `PasskeyErrorCategory`, `PasskeyError`, `PasskeyException`, `PasskeyErrorNormalizer`.
- Configuration: `PasskeyConfiguration`, `PasskeyFeatures`, `PasskeyBranding`, `PasskeyMessages`, each with validation.
- `UnsupportedPasskeyAuthenticator`, the default until a platform authenticator is registered.
- Registration: `ServiceRegistry.AddPasskeyClient` and `IServiceCollection.AddBamPasskeys`.
- `bam.maui.tests` (bam.test): 22 unit tests.

Review round 2
- A probe the caller cancelled is cancelled whatever the authenticator threw; the token decides, not the exception type.
- Once the caller cancels, the operation reports `UserCancelled` even when the failure looks like a timeout.
- A stray cancellation from a probe is logged as a failed probe.
- `RelyingPartyId` refuses IP addresses and all-digit names.
- A plain `net10.0` target, and a `bam.base` reference resolved through `$(BamFrameworkRoot)`.

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

Decisions
- Platform integrations are the library's own, behind `IPasskeyAuthenticator`. MAUI's Essentials passkey API needs .NET 11; bamtk is on .NET 10.
- WebAuthn data is carried as standard JSON, not parsed into typed fields, so the backend's HTTP contract is the only contract and the app doesn't reference bam.useraccounts.
- Colours in `PasskeyBranding` are hex strings so configuration has no UI dependency.

Outstanding
- No platform authenticator, HTTP backend or views yet (phases 2 to 5).
- `bam.maui` is not in `bamtk.sln` yet; that comes with Phase 2.
