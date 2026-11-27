# Passkey Sample

A small app built on `Bam.Maui.Passkeys` to show the white-label passkey screens on a real device:
- sign up and sign in;
- passkey management: add, rename, remove, move the account key;
- lost-device recovery.

Its brand comes from one settings file per deployment level: the sign-in domain, the account endpoint, the name and the colours. A second brand is a second settings file, not a code change.

## Install
- **Android 9 (API 28) or later, with Google Play services:**
  1. Download `passkey-sample-<version>.apk` from the repository's Releases page.
  2. Open it on the phone and allow installs from that source.
- **Windows 10 or 11:**
  1. Download `passkey-sample-<version>-windows-x64.zip` and unzip it anywhere. It's self-contained: no .NET install is needed.
  2. Run `bam.maui.sample.exe`.
  3. Check the download against the SHA-256 in the release notes. The zip isn't code-signed.

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

## Levels and settings
Each deployment level is its own relying party, so a passkey made on one level never signs in on another (bam.useraccounts#37). The build picks a level's settings file and package id:

| Level | Settings file | Sign-in domain | Account endpoint | Package |
|---|---|---|---|---|
| dev (Debug builds) | `samplesettings.dev.json` | `dev.whoiam.is` | `https://dev.whoiam.is/account/` | `net.bamapps.passkeysample.dev` |
| test | `samplesettings.test.json` | `test.whoiam.is` | `https://test.whoiam.is/account/` | `net.bamapps.passkeysample.test` |
| prod (Release builds) | `samplesettings.prod.json` | `whoiam.is` | `https://www.whoiam.is/account/` | `net.bamapps.passkeysample` |

- Build another level with `-p:BamSampleLevel=dev|test|prod`.
- dev and test carry a package suffix, so all three install side by side.
- Each level's domain serves its own `assetlinks.json`.

Unknown keys in a settings file are refused, so a typo fails at start-up instead of being ignored.

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

The `sample` workflow builds both on every pull request, read-only and with no secrets. A `sample-v*` tag on a commit that's on `main` runs the release job, which publishes a Release. That job alone has write access and the signing key, from the `sample-release` environment. The release notes give the bamtk commit and each asset's SHA-256.

Release builds are signed with the key whose fingerprint is in the sign-in domain's `assetlinks.json`:
- **CI:** the key comes from repository secrets.
- **Local:** set `BAM_SAMPLE_KEYSTORE`, `BAM_SAMPLE_KEY_ALIAS`, `BAM_SAMPLE_KEYSTORE_PASSWORD` and `BAM_SAMPLE_KEY_PASSWORD`.
- **Without them** a Release build is debug-signed, and Android won't offer passkeys for the domain.
