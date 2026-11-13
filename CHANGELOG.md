# Changelog

## Unreleased

### Key-set handover (bam.maui#7, against bam.useraccounts#20)

Added
- `IPasskeyClient.HandOverKeySetAsync(id, heirId?)`: moves the account's key set off the passkey that holds it to another of the account's passkeys, then removes it. The backend issues assertion options for the passkey being removed, the system prompt runs them, and the backend verifies before anything changes. Gated on `EnableManagement`; refuses an heir that is the passkey being removed before asking anyone.
- `IPasskeyClient.DeletePasskeyAsync(id, PasskeyKeySetRemoval)` and the matching `IPasskeyBackend` member. `KeepKeySet` removes the key-set passkey without moving the key set, for a lost device.
- `IPasskeyBackend.BeginKeySetHandoverAsync` and `CompleteKeySetHandoverAsync`, and `PasskeyHandoverChallenge`. `HttpPasskeyBackend` posts `passkeys/{id}/handover/begin` and `.../complete`, sends `?keySet=keep`, maps the new refusal codes (`KeySetHandoverRequired`, `LastCredential`, `InvalidHeir`, `NotKeySet`, `RotationUnavailable`, `KeySetChanged`, `HeirGone` to `InvalidOptions`; `HandoverExpired` to `ExpiredChallenge`; `ProofRefused` to `ServerVerificationFailed`), and clears its cookie jar when a handover ends the session.
- The management screen tags the key-set passkey "Account key" and notes it in the removal confirmation. When the backend asks for a handover it opens a "Move your account key" panel (`ShowsHandover`, `HeirChoices`, `SelectedHeir`, `MoveAccountKeyCommand`, `RemoveWithoutMovingCommand`, `CancelHandoverCommand`). Each refusal is worded on `PasskeyText`; one that shows the list changed reloads it on Back.
- `PasskeyFlowViewModel.AfterBackAsync`, a hook that runs after Back leaves an error screen.
- 8 unit tests (100 in all): suites `pkks` and `pkak`.

Changed (breaking for other implementations of the interfaces)
- `IPasskeyBackend` and `IPasskeyClient` have the new members above. The old `DeletePasskeyAsync(id)` stays, as a default interface method that calls the new overload with `PasskeyKeySetRemoval.Default`.

Not yet
- The lost-device recovery (`keyset/recovery/*`, `pendingRecovery`, `keySetRecoverable`; contract on bam.maui#7) waits for its bam.useraccounts PR.

### Phase 5: passkey management (bam.maui#18)

Added
- `PasskeyManagementView`, `PasskeyManagementPage` and `PasskeyManagementViewModel`. They list the account's passkeys (name, added, last used, the session's own, and the device only when the backend sends one), add another with an optional name, rename inline, remove after a confirmation that warns about the session's own passkey, and offer Refresh and Sign out. An account with none gets an empty state. The last passkey's Remove is off.
- `IPasskeyClient.RenamePasskeyAsync`, `SignOutAsync` and `SaveProfileAsync`, with the matching `IPasskeyBackend` members. `HttpPasskeyBackend` posts `passkeys/{id}/name`, `signout` and `profile`.
- `PasskeySignOut` (`SignedOut`, `NextUrl`) and `PasskeyProfile` (`DisplayName`, `Email`).
- `PasskeySignIn.ProfileCreated`, read from `registration/complete`'s `profileCreated` (true when absent). When it is false the registration success screen offers to save the profile (`NeedsProfile`, `ShowsProfileStep`, `SaveProfileCommand`). Continue still works without saving.
- `PasskeyCredential.IsCurrent` and `IsRegisteredKeySet`, from the list's `current` and `registeredKeySet`.
- `IPasskeyNavigator.SignedOutAsync`, a default interface method that calls `SignInRequestedAsync`.
- `PasskeyFlow.Management` and `PasskeyFlow.Profile`, their failure headings, and `PasskeyRecoveryAction.SignInAgain`, offered when the session has gone (`NotSignedIn`) in those flows.
- The sign-in screen shows a signed-in panel ("You're signed in to {app}", a spinner) while the app's `SignedInAsync` runs. Closes bam.maui#17.
- The screen strings for all of this on `PasskeyText`.
- 16 unit tests (90 in all): suites `pkhm`, `pkcm`, `pkmv`, `pkho`, and one more in `pkrc`.

Changed (breaking for other implementations of the interfaces)
- `IPasskeyClient.AddPasskeyAsync` takes an optional name and returns the new passkey's id.
- `IPasskeyClient.DeletePasskeyAsync` and `IPasskeyBackend.DeletePasskeyAsync` return `PasskeySignOut`, because removing the passkey a session signed in with ends that session.
- `IPasskeyBackend.CompleteAdditionalRegistrationAsync` takes the name and returns the id.
- `IPasskeyBackend` has `RenamePasskeyAsync`, `SignOutAsync` and `SaveProfileAsync`.
- The registration screen's `DoneCommand` runs only on the success screen.

Fixed
- A sign-out, or removing the session's passkey, clears the cookie jar `HttpPasskeyBackend.Create` made, so the old session cookie isn't sent again. A host's own `HttpClient` has to do this itself (the README says so).

Review round 1 (bam.maui#19)
- Sign-out clears the cookie jar however the call ends: offline, a timeout, a 5xx, or an answer without `signedOut`. Before, a failed sign-out left a valid session on the device.
- The management screen clears the account before it calls the app's `SignedOutAsync`, and shows "You're signed out" with a spinner while that runs (`ShowsSigningOut`, `SignedOutTitle`, `SigningOutText`, `PasskeyText.SignedOutTitle` and `SigningOut`). The screen was blank during the hand-off, and when the navigator threw, Back brought back the ended session's list.
- A session found gone on the management screen (a failure that offers "Sign in again") takes the list off the screen.
- The README says the management page needs `EnableManagement`. With it off, the app signs out through `IPasskeyClient.SignOutAsync`.
- 2 more unit tests (92 in all).

Review round 2 (bam.maui#19)
- A sign-out that fails signs out on the device anyway. The management screen clears the account before it calls sign-out, and on a failed result calls `SignedOutAsync` with `SignedOut` true and no next URL. The failure is logged by category. Before, the cookie was gone, but Back brought the list back and the app still thought the person was signed in. `IPasskeyClient.SignOutAsync` and `IPasskeyBackend.SignOutAsync` now say the session ends locally however the call ends.
- The list leaves the screen on any `NotSignedIn` failure, keyed on the error (`PasskeyFlowViewModel.LastError`), not on whether "Sign in again" is offered. An app that doesn't sign in with passkeys kept the list and the open editor.
- `PasskeyFlowViewModel.LastError` and `Logger` are protected members for derived screens.
- The sign-out and lost-session tests cover both (92 in all).

Decisions
- Adding, renaming and removing stay behind `EnableManagement`. Sign-out and saving the profile don't: a signed-in person can always leave, and finish an account they already have.
- The server's `ProfileNotSaved` maps to `Unknown`: the request was fine, so there is nothing for the user to change.
- No device details are made up. The row shows a device only when the backend sends one, and bam.useraccounts doesn't today.
- Removal reloads the list after it works, instead of editing it locally, so the screen shows what the server holds.
- `registeredKeySet` is read and kept on the model but not shown; nothing on screen needs it yet.

Outstanding
- The management screen compiles for every target but hasn't been rendered on a device.
- Registration's `ceremonyId` (bam.useraccounts#23) isn't sent yet.

### Phase 4: white-label screens (bam.maui#10)

Added
- `PasskeySignInView` and `PasskeyRegistrationView`, with ready-made `PasskeySignInPage` and `PasskeyRegistrationPage`, built in C# on a shared `PasskeyScreenLayout` (brand and content, one column on narrow windows, side by side from 840 wide) and `PasskeyStatusView` (progress, cancel, and the failure screen).
- `PasskeySignInViewModel` and `PasskeyRegistrationViewModel` over `IPasskeyClient`, sharing `PasskeyFlowViewModel` (`Ready`, `Working`, `Succeeded`, `Failed`, cancel and try again) and `PasskeyCommand`.
- `PasskeyRecovery`, which maps a failed ceremony to a heading, the message, a hint and the ways out (`PasskeyRecoveryPlan`, `PasskeyRecoveryAction`). A cancellation is quiet: the form stays and shows a note.
- `IPasskeyNavigator`, the app's side of leaving a flow.
- `PasskeyTheme` and `PasskeyResourceKeys`: the brand as a `ResourceDictionary`, light and dark, following the device when `ThemeMode` is `System`, with black or white text on the primary colour by WCAG contrast.
- `PasskeyText` (every on-screen string, validated) and `PasskeyExperience` (`SignInMode`, `AlternativeSignInLabel`, `OfferRegistration`, `AskForEmail`, `AskForInviteCode`), both on `PasskeyConfiguration`.
- `AddPasskeyUi` and `AddBamPasskeyUi`.
- 17 unit tests (65 at the first review): suites `pkrc`, `pksi`, `pkrv`, `pkth`, plus screen-text and screen-registration tests in `pkcf` and `pkrg`.

Review round 1 (bam.maui#16)
- Every failure screen ends with Back (`PasskeyRecoveryAction.Back`, `BackCommand`, `PasskeyText.Back`), which returns to the form with the fields kept. No failure screen is a dead end, and a test checks every category, flow and configuration for it.
- `PasskeyRecovery.For` takes `typedInput`. A failure caused by what the user typed (`InvalidOptions`, `NoCredentials`, and a refused sign-up in registration) no longer offers Try again, which sent the same username or fields again. The user goes Back and changes them.
- Sign-in goes back to the form once the app's `SignedInAsync` returns, and when the page appears again. A cached page shown after sign-out was blank. Registration clears its fields after Done.
- A failure in the app's `IPasskeyNavigator` is caught, logged by exception type, and shown as an `Unknown` failure whose Try again repeats the call. It used to escape `async void` and crash the app, or leave a blank success screen. Back from a failed Done returns to the success screen, because the account exists. The screens take an optional `ILogger`, which `AddPasskeyUi` passes when the registry has one.
- `ShowsForm` is removed (no view used it). `Dismiss` refreshes the commands.
- `IPasskeyClient` says that `PasskeyError.Message` is shown to the user as it is, so another implementation must take it from configured text.
- 6 more unit tests (71 in all).

Review round 2 (bam.maui#16)
- The device check's failure screen no longer offers Back (`PasskeyRecovery.For(..., replacesForm: true)`). Back went to a form whose passkey buttons were off with nothing saying why, and the device-setup re-check was lost. The screen now keeps the reason up, and its Try again still checks again.
- A navigation that works from an error screen that offers Back clears the error, and `Dismiss` drops the error's Try again. A retried "Create account" that worked used to leave the screen on "something went wrong".
- 1 more unit test, and the probe, recovery and links tests follow Back and the links through (72 in all).

Review round 3 (bam.maui#16)
- The device check's failure is the sign-in screen's state, not a one-off flag. While it stands, `Dismiss` (Back, and the clear after a working link) returns to the device-check screen with its reason and its re-check. Back from a link the app couldn't follow used to land on the dead form. `RestoreForm` ends it when a check succeeds.
- A link that finishes after a newer error appeared no longer clears that error.
- The README recommends `AlternativeSignInLabel` for devices that can't do passkeys.
- The blank sign-in screen while `SignedInAsync` runs is bam.maui#17, for Phase 5.
- 1 more unit test (73 in all).

Review round 4 (bam.maui#16)
- A unit test pins the "leave a newer error alone" guard: a slow link that finishes after a second link failed leaves the second error up (74 in all).

Fixed (nit from the bam.maui#9 review)
- A transport failure while the caller's own token is cancelled is reported as a cancellation, not `NetworkFailure`. Some handlers fail the body read with an `IOException` instead of an `OperationCanceledException` when the request is aborted.

Decisions
- Views are built in C#, not XAML, so the library has no XAML compilation step in any target.
- The flow, recovery and navigation live in view models that don't touch MAUI controls, so they are tested without a device. An app can bind its own XAML to them.
- The library assumes no navigation framework. `IPasskeyNavigator` lets Shell, `NavigationPage` and `Window.Page` apps each do their own thing.
- Registration success is shown only after the backend's complete call returns; the success screen's Continue is what calls `SignedInAsync`.
- The profile step (`POST /account/profile` when `profileCreated` is false, bam.useraccounts#22) is left to Phase 5 with account management. `profileCreated` isn't on the `IPasskeyBackend` seam yet.

Outstanding
- The views compile for every target but haven't been rendered on a device. The view-model tests cover what the screens do.
- Screen-reader and dynamic-type behaviour need checking on each platform.

### Builds without the MAUI workload (bam.maui#12)

Changed
- `UseMaui` and `SingleProject` are set only when a platform target is in `TargetFrameworks`. A `net10.0`-only build (inside `bamtk.sln`, with `-p:BamPlatformTargets=false`, or any build on Linux) restores on an SDK with no workloads.
- `-p:BamPlatformTargets=false` turns the platform targets off, as building inside `bamtk.sln` does. bamtest passes it to the test projects it builds (bamtest#3).
- Only a global `BamPlatformTargets` (`-p:` or a parent build's properties) counts. A value from the environment, in any letter case, or from a props file is ignored, with warning `BAMMAUI001`. An exported variable would reach an app's build too and silently give it the `net10.0` build, which has no native authenticator. An explicit `-p:` wins even when the variable is also exported. The warning runs as an initial target, so it shows before the SDK's workload check and during restore.

### Phase 3: HTTP backend (bam.maui#6)

Added
- `HttpPasskeyBackend`, an `IPasskeyBackend` over the bam account endpoints (`bam.useraccounts.web`), with `PasskeyHttpBackendOptions` and `AddHttpPasskeyBackend`.
- `PasskeySessionCookieJar`, which keeps the session cookie in front of the platform's HTTP handler.
- `PasskeyErrorCategory.NotSignedIn` and `RateLimited`, with messages.
- `PasskeyException.BackendCode` and `PasskeyError.BackendCode`; the normalizer carries the code across.
- 9 unit tests (48 in all): suite `pkhb`.

Review round 1 (bam.maui#9)
- `PasskeyHttpBackendOptions.Timeout` now bounds the whole exchange, body read included, for the backend's own client and for a host's `HttpClient`; a timeout is `NetworkFailure`.
- An empty or non-object success body on `GET passkeys` (and for the optional members of any response) is `InvalidServerResponse`, not an unhandled exception.
- The platform handler's settings (redirects off, cookies off) are pinned by a test; the README says a host's own `HttpClient` must not follow redirects.

Review round 2 (bam.maui#9)
- A connection that fails before the response (`HttpRequestException`) or drops mid-body (`IOException`, including `HttpIOException`) is `NetworkFailure`. The response is disposed. The transport exception is kept as the inner exception, and its text is not quoted.

Decisions
- BCL `HttpClient`. bamtk's `bam.protocol.client` speaks the signed bam protocol, not a cookie-session JSON API, so it doesn't fit here.
- The session cookie is kept by the library's own `DelegatingHandler`, not by the platform handler's cookie support, which differs between Android, Apple and Windows.
- The endpoint's code, not its message, decides the category. Sign-up refusals share `InvalidOptions` and are told apart by `BackendCode`, rather than one category each.
- The account endpoint must be the relying party or a subdomain of it.

Outstanding
- Not run against a live host yet.
- Not on the seam yet: rename, sign-out, the handle availability check, `profileCreated`, and the delete response's `signedOut`. These come with phases 4 and 5. Moving the key set when the key-set passkey is removed is bam.maui#7.

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
