# bam.maui

MAUI building blocks for the Bam toolkit. Today that is one thing: `Bam.Maui.Passkeys`, a white-label passkey client for .NET MAUI apps.

## Bam.Maui.Passkeys

Apps use it to register an account with a passkey, sign in (with or without a username) and manage passkeys. Three parties are involved and each owns one thing:

- The operating system owns the ceremony: the biometric prompt, the credential picker, the key. These prompts can't be branded or re-skinned, and the library doesn't try.
- The backend owns verification: it issues every challenge and decides whether a ceremony succeeded. The app never verifies a signature.
- The library owns what's in between: orchestration, error handling, configuration, and the branded screens around the prompts.

### Status

Phases 1 to 5 of 7 are in place. Tracking issue: BryanApellanes/bam.maui#1.

| Phase | What | State |
|---|---|---|
| 1 | Contracts, configuration, orchestration, DI, unit tests | Done |
| 2 | Native authenticators (Android, iOS, Mac Catalyst, Windows) | Done. Compiled and unit tested; no ceremony has been run on a device yet (see [Manual verification](#manual-verification)) |
| 3 | HTTP backend for bam.useraccounts.web | Done. Tested against fake endpoints; not yet run against a live host |
| 4 | White-label views and theming | Done. View models, recovery and theme unit tested; the views compile for every target but haven't been rendered on a device yet |
| 5 | Passkey management UI | Done. List, add, rename, remove, sign out and the profile step, unit tested through the view models; not yet rendered on a device |
| 6 | Older Android, platform matrix | Not started |
| 7 | Hardening, docs, sample app | Not started |

The library never reports support it can't deliver: `IsSupportedAsync` is true only when the platform's authenticator says a ceremony can run. `HttpPasskeyBackend` talks to a bam host's account endpoints; an app with another server binds its own `IPasskeyBackend`.

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

Inside `bamtk.sln` the library builds `net10.0` only. `run-tests.sh` builds that solution into one output folder, and several targets of one project would overwrite each other there. The same happens with `-p:BamPlatformTargets=false`, which bamtest passes to the test projects it builds (bamtest#3). Only a global property counts (`-p:`, or a parent build's properties). A `BamPlatformTargets` exported into the environment would reach every build in that shell, an app's included, so bam.maui ignores it, whatever its letter case, and warns (`BAMMAUI001`). An explicit `-p:` still wins when the variable is exported too. A `net10.0`-only build (this one, or any build on Linux, where the platform targets are skipped) doesn't set `UseMaui`, so it needs no MAUI workload, only the .NET SDK. Building the project directly, or from an app's solution, builds every target. `-p:BamMauiPlatformTargets=true` or `false` overrides all of this.

### Using it

```csharp
ServiceRegistry registry = new ServiceRegistry();
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

registry.AddHttpPasskeyBackend(configuration);                   // the bam account endpoints at https://example.com/account/
builder.Services.AddBamPasskeys(registry, configuration);          // in MauiProgram
```

A server that isn't a bam host gets its own `IPasskeyBackend`, bound before `IPasskeyClient` is resolved (`registry.For<IPasskeyBackend>().Use(new MyBackend())`).

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

### HTTP backend

`HttpPasskeyBackend` is the client for the account endpoints of `bam.useraccounts.web` (its README, "Endpoints"). The HTTP contract is the boundary, and bam.maui doesn't reference bam.useraccounts.

| `IPasskeyBackend` member | Request |
|---|---|
| `BeginRegistrationAsync` | `POST passkey/registration/begin` `{ handle, displayName, email?, inviteCode? }` |
| `CompleteRegistrationAsync` | `POST passkey/registration/complete`, the same fields plus `credential` |
| `BeginAdditionalRegistrationAsync` | `POST passkey/additional/begin` `{}` |
| `CompleteAdditionalRegistrationAsync` | `POST passkey/additional/complete` `{ credential, name? }`, returns the new `credentialId` |
| `BeginAuthenticationAsync` | `POST passkey/assertion/begin` `{ handle? }` |
| `CompleteAuthenticationAsync` | `POST passkey/assertion/complete` `{ ceremonyId, credential }` |
| `GetPasskeysAsync` | `GET passkeys`, reading `current` and `registeredKeySet` when present |
| `RenamePasskeyAsync` | `POST passkeys/{id}/name` `{ name }` (`null` clears it) |
| `DeletePasskeyAsync` | `DELETE passkeys/{id}`, or `DELETE passkeys/{id}?keySet=keep` with `PasskeyKeySetRemoval.KeepKeySet`; returns `{ signedOut, nextUrl }` |
| `BeginKeySetHandoverAsync` | `POST passkeys/{id}/handover/begin` `{ heir? }`, returns `{ handoverId, options }` |
| `CompleteKeySetHandoverAsync` | `POST passkeys/{id}/handover/complete` `{ handoverId, credential }`, returns `{ signedOut, nextUrl }` |
| `SignOutAsync` | `POST signout` `{}`, returns `{ signedOut, nextUrl }` |
| `SaveProfileAsync` | `POST profile` `{ displayName, email? }` |

`registration/complete` also returns `profileCreated`. When it is false the account exists but its profile wasn't written, and `PasskeySignIn.ProfileCreated` says so (a server that doesn't send it counts as true).

- `PasskeyHttpBackendOptions.AccountEndpoint` defaults to `https://<relying party id>/account/`. It must be https, with no user info, query or fragment, and on the relying party id or a subdomain of it. `Timeout` (30 s) and `MaxResponseBytes` (256 KB) can be changed.
- Every request carries `X-Bam-Account: bam.maui` and no `Origin`, which is how the endpoints recognize a native client.
- The session is the `.Bam.Account` cookie. `HttpPasskeyBackend.Create` keeps it in a cookie jar of its own, in front of the platform's HTTP handler (which runs with cookies and redirects off), so the session works the same on every platform. Passing your own `HttpClient` is also supported. It then has to keep cookies itself, and it must be built with `AllowAutoRedirect = false`: the backend refuses a redirect it sees, but a client that follows redirects never shows it one. Sign-out, and removing the passkey the session signed in with (by a removal or a key-set handover), clear the jar `Create` made. Sign-out clears it however the call ends, even offline or with an answer that isn't the contract; a server that never got the request keeps the session until it expires, but the device no longer holds it. A client you pass in must drop the cookie itself when those calls report `SignedOut`.
- WebAuthn options go to the authenticator byte for byte, and the authenticator's response is sent as `credential` unchanged.
- `nextUrl` is kept only when it is a path on the site. A credential id goes into a path only when it is base64url.

How a refusal maps. The endpoint's `error` code wins over the HTTP status, and it is carried as `PasskeyError.BackendCode`:

| Response | Category |
|---|---|
| `NotSignedIn`, or 401 | `NotSignedIn` |
| `RateLimited`, or 429 | `RateLimited` |
| `BadRequest`, `InvalidHandle`, `HandleTaken`, `EmailRequired`, `InviteRequired`, `NotFound`, `LastPasskey`, `LastCredential`, `KeySetHandoverRequired`, `InvalidHeir`, `NotKeySet`, `RotationUnavailable`, `KeySetChanged`, `HeirGone`, or a bare 400 | `InvalidOptions` |
| `HandoverExpired` | `ExpiredChallenge` |
| `ProfileNotSaved` | `Unknown` |
| `ProofRefused`, `RequestRefused`, `SignInFailed`, `SignUpFailed`, `SignUpClosed`, `PasskeyNotAdded`, or another 4xx | `ServerVerificationFailed` |
| 502, 503, 504, or a transport failure or timeout | `NetworkFailure` |
| A redirect, a bare 404 or 405, another 5xx, or a success that isn't the contract (not JSON, a missing member, too large) | `InvalidServerResponse` |

The server's `message` isn't used. A sign-up screen branches on `BackendCode` (for example `HandleTaken` against `InviteRequired`) and shows its own text.

The host must list `https://<rp id>` in `PasskeyOptions.AllowedOrigins` for the iOS, Mac Catalyst and Windows clients (the site's own origin, so usually nothing to add), and the Android app's origin in `PasskeyOptions.AllowedAppOrigins`.

### API

| Member | Does |
|---|---|
| `IsSupportedAsync`, `GetCapabilitiesAsync` | Ask the device, without prompting |
| `RegisterPasskeyAsync(request)` | Create an account with its first passkey |
| `AddPasskeyAsync(name?)` | Add a passkey to the signed-in account; returns its id |
| `AuthenticateAsync()` | Username-less sign-in |
| `AuthenticateWithUsernameAsync(handle)` | Username-first sign-in |
| `GetRegisteredPasskeysAsync()`, `RenamePasskeyAsync(id, name?)`, `DeletePasskeyAsync(id)` | Manage the signed-in account's passkeys. Removal returns a `PasskeySignOut`, which says whether it ended the session |
| `DeletePasskeyAsync(id, PasskeyKeySetRemoval.KeepKeySet)` | Remove the passkey that holds the account's key set without moving it (a lost device). Sign-in with it stops at once, but its key still counts as the account key until the key set is replaced (the lost-device recovery, bam.maui#20) |
| `HandOverKeySetAsync(id, heirId?)` | Move the key set off the passkey that holds it, to another of the account's passkeys (this session's when `heirId` is null), and remove it. The system prompt asks for the passkey being removed |
| `SignOutAsync()` | End the session |
| `SaveProfileAsync(profile)` | Save the display name and email when sign-up couldn't |
| `GetPasskeyStatusAsync()` | Capabilities plus the passkey count when it can be read |

Adding, renaming, removing and handing over the key set are off unless `EnableManagement` is on. Sign-out and the profile aren't gated: a signed-in person can always leave, and finish an account they already have. Names are trimmed, and a blank one means "no name".

No ceremony or management method throws for an expected failure. Each returns a `PasskeyResult`. The three probes (`IsSupportedAsync`, `GetCapabilitiesAsync`, `GetPasskeyStatusAsync`) throw `OperationCanceledException` when the caller cancels, and nothing else: a cancelled probe says nothing about the device, so it is never reported as "not supported".

### Errors

`PasskeyErrorCategory` is the one vocabulary on every platform: `UserCancelled`, `NoCredentials`, `UnsupportedOperatingSystem`, `MissingCredentialProvider`, `DeviceSecurityNotConfigured`, `NetworkFailure`, `InvalidServerResponse`, `InvalidOptions`, `ExpiredChallenge`, `ServerVerificationFailed`, `PlatformFailure`, `FeatureDisabled`, `CredentialAlreadyRegistered`, `NotSignedIn`, `RateLimited`, `Unknown`. `PasskeyError.BackendCode` carries the backend's own refusal code when it sent one; it is a code to branch on, not text to show.

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
7. Cancel the token while the prompt is up: the result is `UserCancelled` at once, and the prompt closes. On Android and Apple the result does not wait for the platform to call back. If a credential provider ignores the cancel and the user finishes anyway, a registration can leave a passkey on the device that the server never recorded; it can't sign in, and the user can delete it in the provider.
8. `AddPasskeyAsync` on a device that already holds a passkey for the account: `CredentialAlreadyRegistered` on Android and Windows. On Apple the backend refuses the duplicate instead, because the exclude list isn't passed there yet.
9. Sign in with a username that has no passkey on the device: `NoCredentials` on Android and Windows.
10. Point the app at options for another RP id: `InvalidOptions`, and no system prompt appears.
11. Android only: break `assetlinks.json` and confirm the ceremony fails rather than succeeding.
12. iOS only: remove the associated domain and confirm the ceremony fails with `PlatformFailure`.

Known unknown these steps settle: whether the Apple sheet needs anything beyond the current view controller's window as its anchor. (Android errors are mapped by instance-of checks against the bound exception classes, not by class name or message.)

### Screens

The library ships a sign-in screen, a registration screen and a screen for managing passkeys. Each comes as a view (`PasskeySignInView`, `PasskeyRegistrationView`, `PasskeyManagementView`) to put in a page of your own, and as a ready-made page (`PasskeySignInPage`, `PasskeyRegistrationPage`, `PasskeyManagementPage`). The system prompt itself is the operating system's and isn't styled; the screens are what's around it.

```csharp
builder.Services.AddBamPasskeys(registry, configuration);
builder.Services.AddBamPasskeyUi(registry, new MyNavigator());   // after AddBamPasskeys
```

`IPasskeyNavigator` is how the screens leave a flow. The library assumes no navigation framework, so the app decides what each call does: `SignedInAsync(signIn, flow)` once the backend has verified a ceremony, `RegistrationRequestedAsync` and `SignInRequestedAsync` for the links between the two screens, `AlternativeSignInRequestedAsync` for the app's other way in, and `SignedOutAsync(signOut)` when the session has ended. `SignedOutAsync` has a default that calls `SignInRequestedAsync`, so a navigator written before it still compiles.

- **Sign-in.** `PasskeyExperience.SignInMode` picks the layout: `UsernameLess` (one passkey button), `UsernameFirst` (a username field and Continue), or `Choice` (both, the default). With `EnableUsernameLessAuthentication` off, the screen is always username-first. The page checks the device each time it appears; a device that can't do passkeys gets the unavailable or device-setup screen, and the sign-in buttons stay off.
- **Registration.** What a passkey is and why to use one, the username and name fields, and optional email and invite code fields (`AskForEmail`, `AskForInviteCode`). Only the fields the screen shows are sent. The success screen shows only after the backend has verified the new credential; its Continue button calls `SignedInAsync`.
- **Profile.** When sign-up reports `ProfileCreated` false, the success screen says the details weren't saved and offers to save them (`SaveProfileCommand`, with the name and email the user typed). Continue stays, so the person can leave without saving, and `SignedInAsync` gets `ProfileCreated` false. A refused save is a "Couldn't save your details" failure whose Back returns to the success screen.
- **Managing passkeys.** `PasskeyManagementPage` loads the account's passkeys each time it appears. Each row shows the name (or "Unnamed passkey"), when it was added and last used, "This device's passkey" for the one the session signed in with, and the device only when the backend sends one. The library never makes device details up. A name field and Add passkey add another; Rename opens an inline editor; Remove asks first, and warns when the passkey is the one this session uses. The last passkey can't be removed: its Remove is off, and a server refusal (`LastPasskey`) says why. An account with none shows an empty state with Add passkey. The passkey that holds the account's key set is tagged "Account key" (from the list's `registeredKeySet`), and its removal confirmation says so. When the backend can move key sets it refuses that removal (`KeySetHandoverRequired`), and the screen opens a "Move your account key" panel instead of an error: pick another passkey (it starts on this session's; when that is the one going, nothing is chosen unless only one passkey is left), then "Move key and remove", which asks for the passkey being removed in the system prompt. "Remove without moving" is there for a lost device, and the panel says plainly that the removed passkey's key still counts as the account key until it is replaced. A refused heir goes back to the panel; a list that changed underneath (`KeySetChanged`, `HeirGone`, `NotFound`, and a `LastPasskey` or `NotFound` refusal of a removal without moving) goes back to a freshly loaded list; an expired handover offers Try again. A move that ends the session signs out the usual way. Refresh reloads, and Sign out ends the session. When sign-out or a removal ends the session, the screen clears the account first, then calls `SignedOutAsync`, and shows "You're signed out" while that runs. If the app's navigation throws, Back from its failure finds an empty screen, not the old list. A sign-out that fails (offline, a server error) still signs out on the device: the client has already forgotten the session, so the screen clears and `SignedOutAsync` is called with no next URL. If the session has already gone (`NotSignedIn`), the list is taken off the screen, and the failure offers "Sign in again" (or Try again when the app doesn't sign in with passkeys). The screen needs `EnableManagement`: with it off the list doesn't load, and the screen shows only the unavailable failure, with no Sign out. An app with management off signs out through `IPasskeyClient.SignOutAsync` and shouldn't show this page.
- **Signed in.** While the app's `SignedInAsync` runs, the sign-in screen shows "You're signed in to {app}" and a spinner instead of a blank page (bam.maui#17).
- **Progress.** While the system prompt is up the screen shows the working text and a Cancel button, which cancels the ceremony.
- **After success.** Sign-in goes back to the form as soon as the app's `SignedInAsync` returns, and again whenever the page appears, so a cached page the app returns to after sign-out isn't blank. Registration clears its fields after Done.
- **The app's navigation failing.** If an `IPasskeyNavigator` call throws, the screen catches it, logs the exception type (never its text), and shows an `Unknown` failure whose Try again makes the same call again. Nothing throws through a command.
- **Errors.** `PasskeyRecovery.For(error, flow, configuration)` decides what a failure screen shows. It's one pure function, so every category's screen is decided in one place:

| Failure | Heading | Ways out (all end with Back, except the device check's) |
|---|---|---|
| `UserCancelled` | None: the form stays, with a short note | |
| `UnsupportedOperatingSystem`, `FeatureDisabled` | Unavailable | Back |
| `MissingCredentialProvider`, `DeviceSecurityNotConfigured` | Device setup, with a hint | Try again |
| `NetworkFailure` | Offline | Try again |
| `CredentialAlreadyRegistered` | Couldn't create the passkey | Sign in instead |
| `NoCredentials` when signing in | Couldn't sign in | Try again, create an account |
| Anything else | Couldn't sign in / create the passkey | Try again |

Back returns to the form with what the user typed kept. Every failure screen offers it, so none is a dead end. The one exception is the device check that runs when the sign-in screen appears: its unavailable or device-setup screen takes the place of a form whose passkey buttons can't work, so it has no Back. It keeps the reason on screen, and the device-setup screen's Try again checks the device again. Back from a later error on that screen (a link the app couldn't follow) returns to the device check, not the form, until a check succeeds. On a device that can't do passkeys, the screen's only way forward is the app's other way in, so set `AlternativeSignInLabel` if the app has one. A link that works from a failure screen (a retried link, the app's other way in) clears the error, so the screen isn't left on it when the app comes back. It leaves a newer error alone. When the failure came from what the user typed (`InvalidOptions`, `NoCredentials`, or a refused sign-up, `ServerVerificationFailed`, in registration), Try again isn't offered, because it would send the same input. The user goes Back, changes it, and submits again. Registration failures also offer "sign in instead" when authentication is on (except on a device that can't do passkeys). Every failure screen offers the app's other way in when `AlternativeSignInLabel` is set.

**Wording.** `PasskeyText` holds every string the screens show. `{0}` in a heading is the app name. Replace members to reword or translate; each must have text. Error messages still come from `PasskeyMessages`.

**Branding.** `PasskeyBranding` holds the application name, logo, colours (hex strings), dark-scheme colours, corner radius, font family and theme mode. It's validated at registration, and nothing in the library hardcodes a brand. `PasskeyTheme` turns it into a `ResourceDictionary` that every screen merges, under the `PasskeyResourceKeys` names (`Passkey.Primary`, `Passkey.Background`, `Passkey.Text` and so on). Change an entry to restyle every screen. Text on the primary colour is black or white, whichever has the higher WCAG contrast. `ThemeMode` `System` follows the device's light or dark setting as it changes; `Light` and `Dark` fix it.

**Layout and accessibility.** On a narrow window the brand sits above the content in one column of readable width; from 840 wide it sits beside it. Headings, fields, buttons, the logo and the progress spinner carry semantic descriptions for screen readers.

The screens' view models (`PasskeySignInViewModel`, `PasskeyRegistrationViewModel`, `PasskeyManagementViewModel`) are public, so an app that wants its own layout can bind its own XAML to them and keep the flow, the recovery and the navigation.

### Security

- The app verifies nothing. A ceremony has succeeded only when the backend's complete call returns, and the signed-in identity is taken from the backend's answer, never from what the user typed. A completion that names no account is treated as a failure.
- Challenges are generated, expired and consumed by the backend. The library holds one only for the length of a ceremony.
- Private keys and biometric data never reach the app; the platform APIs don't expose them.
- Nothing sensitive is logged. Failures are logged by operation, category and exception type names. Exception messages and WebAuthn payloads are not. The payload wrappers, the registration request (email, invite code) and the authentication challenge (ceremony id) print only their type name. Tests pin all of this.
- `PasskeyFeatures` switches decide what the app offers. They are not access control; the backend enforces what an account may do.
- `PasskeySignIn.NextUrl` is whatever the backend sent. `HttpPasskeyBackend` keeps it only when it is a path on the site; a custom backend should do the same, and the app should still treat it as a path, never a URL.
- `HttpPasskeyBackend` only talks to the relying party or a subdomain of it, over https. The client `Create` builds doesn't follow redirects; a host's own `HttpClient` must be built the same way. Refusals are reported by code and status, never by quoting a body.
- `PasskeyHttpBackendOptions.Timeout` bounds each whole exchange, body included, on either kind of client, so a server that sends headers and then stalls is cut off and reported as `NetworkFailure`.
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

Suites: `pkcf` configuration, branding, capabilities and what the models print; `pken` error normalization; `pkcl` client orchestration, cancellation, feature switches and management; `pkdg` where a cancellation came from, and what reaches the log; `pkrg` registering the client and the screens in a registry; `pkwj` reading options and writing response JSON; `pkwn` the Windows authenticator and its interop layout; `pkpe` the Android, Apple and Windows error maps; `pkpc` a callback ceremony ending from the caller's token; `pkhb` the HTTP backend against fake account endpoints; `pkrc` the recovery plan for every error category; `pksi` the sign-in screen's view model; `pkrv` the registration screen's view model; `pkth` the theme; `pkhm` the HTTP management, sign-out and profile calls; `pkcm` the client's management calls and their gates; `pkmv` the management screen's view model; `pkho` the signed-in hand-off and the profile step. The screen tests drive the view models, not rendered views.

### Dependencies

- `Microsoft.Maui.Controls` 10.0.20
- `Xamarin.AndroidX.Credentials` 1.5.0.3, Android target only. It is Microsoft's binding of `androidx.credentials`; bamtk has nothing that does this. 1.5.0.3 is the newest version whose AndroidX dependencies agree with MAUI 10.0.20
- `bam.base` (`ServiceRegistry`, `Bam.Logging.ILogger`), which brings `Newtonsoft.Json`; the error normalizer recognises its exceptions
