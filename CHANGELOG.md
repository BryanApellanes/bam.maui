# Changelog

## Unreleased

### Phase 1: passkey foundation (bam.maui#2)

Added
- `Bam.Maui.Passkeys`: `IPasskeyService`, `IPasskeyAuthenticator`, `IPasskeyBackend` and the orchestrating `PasskeyService`.
- Models: WebAuthn JSON wrappers, registration request, challenges, `PasskeySignIn`, `PasskeyCredential`, `PasskeyCapabilities`, `PasskeyStatus`, `PasskeyResult`.
- Errors: `PasskeyErrorCategory`, `PasskeyError`, `PasskeyException`, `PasskeyErrorNormalizer`.
- Configuration: `PasskeyConfiguration`, `PasskeyFeatures`, `PasskeyBranding`, `PasskeyMessages`, each with validation.
- `UnsupportedPasskeyAuthenticator`, the default until a platform authenticator is registered.
- Registration: `ServiceRegistry.AddPasskeyClient` and `IServiceCollection.AddBamPasskeys`.
- `bam.maui.tests` (bam.test): 18 unit tests.
- A plain `net10.0` target, and a `bam.base` reference resolved through `$(BamFrameworkRoot)`.

Changed
- Minimum iOS and Mac Catalyst versions raised to 15.0, the minimum MAUI 10 supports.
- Platform targets are skipped on Linux.

Removed
- The scaffold `Class1` and `PlatformClass1` files, and the unused `Microsoft.Maui.Controls.Compatibility` reference.

Decisions
- Platform integrations are the library's own, behind `IPasskeyAuthenticator`. MAUI's Essentials passkey API needs .NET 11; bamtk is on .NET 10.
- WebAuthn data is carried as standard JSON, not parsed into typed fields, so the backend's HTTP contract is the only contract and the app doesn't reference bam.useraccounts.
- Colours in `PasskeyBranding` are hex strings so configuration has no UI dependency.

Outstanding
- No platform authenticator, HTTP backend or views yet (phases 2 to 5).
- `bam.maui` is not in `bamtk.sln` yet; that comes with Phase 2.
