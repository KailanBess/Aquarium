# Aquarium

Native C# / .NET 8 / WinUI 3 desktop hub. The private Store stays Aquarium-only. The separate Updates destination can check supported outside apps.

## Build and run

Requires Windows 10 build 19041+ x64, .NET 8 SDK, Visual Studio 2022 17.8+ WinUI C# / Windows application development tooling, and Windows SDK 10.0.26100. NuGet access is required. Windows App SDK 1.6.250205002 is pinned; this is not a claim it is the latest release.

```powershell
dotnet restore .\src\Aquarium\Aquarium.csproj
dotnet build .\src\Aquarium\Aquarium.csproj -c Debug -p:Platform=x64
dotnet run --project .\src\Aquarium\Aquarium.csproj -c Debug -p:Platform=x64
```

Or open the csproj in Visual Studio and run x64. Publish a self-contained, unpackaged folder:

```powershell
dotnet publish .\src\Aquarium\Aquarium.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o .\publish
.\publish\Aquarium.exe
```

Keep all publish files together. No MSIX registration or installer is required. [Microsoft deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app?tabs=csharp).

## Features

- Home, searchable Store, Library bookmarks, Downloads, Updates, and Settings.
- Aquatic dark UI retained, with saved Mint / Blue / Coral accents, Ocean / Midnight / Slate backgrounds, and compact collection spacing in Settings.
- Atomic local registry and appearance writes. Invalid files are preserved and their writes disabled.
- Security and Emulation adapters remain pending actual project inspection. FishNet is a separate standalone media player.

Library bookmarks do not mean installed apps. Downloads and Aquarium package installation remain unavailable. No VPN, network-management, or Tailscale features are included. No external existing apps are modified by source authoring.

## Central update ownership

Aquarium is the Aquarium-family update hub. FishNet has no updater. These are the currently implemented providers:

1. **Outside apps / WinGet**: on-demand `winget upgrade --source winget --disable-interactivity` checks. The unparsed report, exit code, and timestamp are displayed. Check failures never become an 'up to date' status. Checks are cancellable and time out after two minutes.
2. **Selected outside-app upgrade**: copy an exact package ID from the report, review the confirmation, then start a visible interactive WinGet upgrade using that ID and exact matching. No bulk update, silent install, or automatic agreement acceptance is used. Aquarium does not track the separate installer's completion. Run another check afterward.
3. **Aquarium / FishNet releases**: query the two allowlisted GitHub repositories for the latest stable release tag. Missing releases and HTTP failures are reported. Tags are release information only: verified package installation, version comparisons, rollback, and self-update replacement are not implemented.

WinGet requires Microsoft App Installer and a usable `winget` community source. If source agreements need review, run `winget upgrade` in a terminal first. Source terms are not automatically accepted. Check output is intentionally not parsed using fragile localized table columns. Microsoft Store-only, portable, custom, unrecognized, or unknown-version apps may be absent. Spotify can be detected when WinGet matches the installed package. No provider can reliably update literally every Windows app. Vendor self-updaters are not disabled.

The Store remains private to Aquarium apps; outside apps are only managed under Updates. User-requested checks contact WinGet's configured community source or GitHub. No background scans, telemetry, or scheduled checks exist. Catalog privacy is product scope, not authentication; repository source is public.

References: [WinGet upgrade](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade), [WinGet source agreements](https://learn.microsoft.com/en-us/windows/package-manager/winget/source).

## Architecture and data

- `MainWindow`: native navigation and catalog cards.
- `LocalStore` / `Models`: bundled catalog, versioned local registry, module and installer contracts.
- `UpdateService` / `UpdatesUi`: centralized release checks and WinGet bridge.
- `Preferences`: appearance editor and persistence, independent from registry state.

Data: `%LOCALAPPDATA%\Aquarium\registry.json` and `appearance.json`. Update results are session-only. Single-instance editing is assumed; cross-process coordination remains future work.

Before connecting existing apps: inspect source/build outputs and privileges; retain standalone operation; define a signed package manifest, digest verification, staging, rollback, and uninstall; record installations only after success; validate entry points before launch. No unreviewed assemblies are loaded into Aquarium.

## Verification

The local authoring machine has no discoverable .NET SDK. XML/JSON structure is statically checked. The Windows build workflow compiles on GitHub Actions; consult its actual status before treating builds as verified. Playback, layout, and WinGet behavior require Windows smoke testing.

Smoke checks: visit every destination, search and restart-test bookmarks; change all appearance options and restart; check updates with/without WinGet, cancel a check, test missing releases/offline failure, and verify no check installs anything. Test one exact outside-app upgrade on a disposable development machine, review the installer and recheck. Corrupt a backed-up registry/preferences file while closed and confirm it is preserved with writes disabled, then restore it.
