# Passkey Sample

A small app built on `Bam.Maui.Passkeys` to show the white-label passkey screens on a real device:
- sign up and sign in;
- passkey management: add, rename, remove, move the account key;
- lost-device recovery.

Its brand comes from one file, `samplesettings.json`: the sign-in domain, the account endpoint, the name and the colours. A second brand is a second settings file, not a code change.

## Install
- **Android 9 (API 28) or later, with Google Play services:**
  1. Download `passkey-sample-<version>.apk` from the repository's Releases page.
  2. Open it on the phone and allow installs from that source.
- **Windows 10 or 11:**
  1. Download `passkey-sample-<version>-windows-x64.zip` and unzip it anywhere.
  2. Run `Bam.Maui.Sample.exe`.

  Passkeys use Windows Hello.
- **iOS and Mac Catalyst** aren't built yet: they need an Apple developer account.

## What each screen shows
| Screen | How to get there | Web twin |
|---|---|---|
| Sign in | The app opens on it | whoiam.is `/account/signin` |
| Create an account | Sign in → "Create an account" | whoiam.is `/account/signup` |
| Signed in | After sign-in or sign-up: the name and handle the server sent | whoiam.is `/account` |
| Passkeys | Signed in → "Manage passkeys": add, rename, remove, move the account key, start or cancel a recovery | whoiam.is `/account` |
| Recovery notice | Sign in while a recovery is waiting | whoiam.is `/account` banner |

## Settings
`samplesettings.json` is built into the app. Unknown keys are refused, so a typo fails at start-up instead of being ignored.

| Key | Default | Meaning |
|---|---|---|
| `relyingPartyId` | `whoiam.is` | The sign-in domain. Passkeys belong to it, and it must serve this app's `assetlinks.json` |
| `accountEndpoint` | `https://<relyingPartyId>/account/` | Where the account endpoints are; must be https on that domain or a subdomain |
| `askForEmail` | `false` | Whether sign-up asks for an email address |
| `branding` | Passkey Sample, blue | Any `PasskeyBranding` member: `applicationName`, `primaryColor`, `secondaryColor`, `backgroundColor`, `textColor` and the rest |

## Build
The sample builds inside bamtk, where the framework libraries are sibling submodules:
```bash
dotnet build bam.maui.sample/bam.maui.sample.csproj -f net10.0-android
dotnet build bam.maui.sample/bam.maui.sample.csproj -f net10.0-windows10.0.19041.0
```

The `sample` workflow builds both on every pull request. A `sample-v*` tag publishes a Release.

Release builds are signed with the key whose fingerprint is in the sign-in domain's `assetlinks.json`:
- **CI:** the key comes from repository secrets.
- **Local:** set `BAM_SAMPLE_KEYSTORE`, `BAM_SAMPLE_KEY_ALIAS`, `BAM_SAMPLE_KEYSTORE_PASSWORD` and `BAM_SAMPLE_KEY_PASSWORD`.
- **Without them** a Release build is debug-signed, and Android won't offer passkeys for the domain.
