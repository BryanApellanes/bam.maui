# bam.maui

A .NET MAUI class library providing cross-platform UI components for the BAM toolkit.

## Overview

bam.maui is a multi-targeted .NET MAUI class library that targets Android, iOS, macOS Catalyst, and Windows. It is structured as a single-project MAUI library with platform-specific code organized under the `Platforms/` directory, following the standard MAUI project conventions.

The project is currently in its initial scaffolding stage. It contains a shared `Class1` and platform-specific `PlatformClass1` stubs for each target platform (Android, iOS, macOS Catalyst, Windows, and Tizen). No actual functionality has been implemented yet.

The library uses .NET MAUI Controls and Compatibility packages at version 10.0.20, targeting the latest .NET 10.0 framework.

## Key Classes

| Class | Description |
|---|---|
| `Class1` | Empty shared class included on all platforms (placeholder) |
| `PlatformClass1` (Android) | Empty platform-specific class for Android |
| `PlatformClass1` (iOS) | Empty platform-specific class for iOS |
| `PlatformClass1` (macOS Catalyst) | Empty platform-specific class for macOS Catalyst |
| `PlatformClass1` (Windows) | Empty platform-specific class for Windows |
| `PlatformClass1` (Tizen) | Empty platform-specific class for Tizen |

## Dependencies

**Package References:**
- `Microsoft.Maui.Controls` 10.0.20
- `Microsoft.Maui.Controls.Compatibility` 10.0.20

**Target Frameworks:**
- `net10.0-android` (min SDK 21.0)
- `net10.0-ios` (min 11.0)
- `net10.0-maccatalyst` (min 13.1)
- `net10.0-windows10.0.19041.0` (Windows only, min 10.0.17763.0)

## Usage Examples

```csharp
// No functional API is available yet.
// Once implemented, the library would be referenced from a MAUI app:
// <ProjectReference Include="path/to/bam.maui.csproj" />
```

## Known Gaps / Not Yet Implemented

- **All classes are empty stubs.** `Class1` and all `PlatformClass1` implementations contain no logic or members.
- No MAUI controls, pages, services, or platform-specific integrations have been built.
- No connection to other BAM toolkit libraries (bam.base, bam.console, etc.).
- The project appears to be a template/placeholder awaiting future development.
