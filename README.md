# Aquarium

Native C# / .NET 8 / WinUI 3 Windows desktop hub with a private Aquarium-only app catalog, GitHub release installation, local Library, Downloads, Updates and customization.

## Complete bundle

The `Aquarium-complete-windows-x64` build artifact includes Aquarium, FishTank and FishNet. Extract the entire folder and run `Aquarium.exe`. Keep every runtime and Bundled file together. On first launch, Aquarium verifies and installs its two included app packages into its managed app folder. A brand-new FishTank profile receives the Aquarium colors using its released custom-palette support; existing appearance and security preferences are never replaced. It never launches them automatically. FishBowl is **not** included and remains an optional install from App install.

FishTank is the inspected public `preview-v0.15.5` release. FishNet 0.2.1 is built from the pinned source commit in the workflow because no public FishNet release exists yet. This does not claim the separate FishTank/FishBowl UI pull requests have been released. These are development builds, not signed production installers.

## App install and Library

1. Open **App install** and choose **Look for published apps**.
2. Aquarium reads public release metadata for `KailanBess/FishTank`, `KailanBess/FishNet` and `KailanBess/FishBowl` only. Draft releases are excluded. FishTank permits published previews because it has no stable release; the other apps use stable releases.
3. Review the version/source and click Install. The confirmation describes the download and local install. GitHub's asset SHA-256 digest is used where available; otherwise a matching release `SHA256SUMS.txt` entry is required.
4. Downloads shows real activity, cancel controls, failures and session history. An installation enters Library only after extraction and registry persistence succeed. Use **Open** to start it.

Latest release checks require internet access and may fail due to GitHub rate limits. Missing releases, missing Windows packages and missing checksums are reported as unavailable. CI artifacts are not represented as public releases in the App install page. For future FishNet releases, publish `FishNet-windows-x64.zip` containing its complete publish directory, with a GitHub digest or matching `SHA256SUMS.txt`. No arbitrary outside repositories are executable store entries.

App versions use separate managed folders under `%LOCALAPPDATA%\Aquarium\Apps`. Failed installs preserve the previous registry entry; successful replacements retain old program folders. Games, media, accounts and application profiles stay in their apps' existing locations. No destructive uninstall or old-version cleanup is implemented yet. Do not move or alter managed folders while apps are running. The initial bundle skips default apps that are already registered.

The download checksum protects integrity from the trusted repository's metadata; it is not independent publisher-signature authentication. Install sources are explicitly allowlisted. Packages reject traversal paths, duplicates, symlinks and excessive sizes/counts; launch paths are contained within managed directories. Apps are started as standalone processes and run as the current Windows user.

## Updates

Aquarium owns the shared update UI. Its Updates page links to managed app installs/updates and can check public Aquarium-family releases. For outside apps it runs read-only `winget upgrade --source winget --disable-interactivity`, displays original output and offers an exact-ID interactive update after confirmation. Checks do not install anything or accept agreements. WinGet cannot cover every app, and vendor self-updaters are not disabled. FishNet has no separate updater. Existing released FishTank/FishBowl behavior is retained.

## Appearance and scope

Ocean/Midnight/Slate backgrounds, Mint/Blue/Coral accents and compact spacing persist in Settings. Store search and saved bookmarks remain available independently from installations. FishTank = Security, FishBowl = Emulation, FishNet = Media. No private VPN, network-management or Tailscale project integration is included.

## Build

Requires Windows 10 build 19041+ x64 and the .NET 8 SDK. Microsoft Windows App SDK 1.8.260921001 and Windows build tools 10.0.26100.4654 are restored from NuGet; no system runtime is required for the complete bundle. Visual Studio is optional. `global.json` selects .NET 8.

```powershell
dotnet build .\src\Aquarium\Aquarium.csproj -c Debug -p:Platform=x64
& .\src\Aquarium\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Aquarium.exe
```

A source-only build has no Bundled folder; it still provides the App install page. The GitHub workflow publishes a complete self-contained hub, builds pinned FishNet source, downloads/verifies the inspected FishTank release, and generates its bundled-app hash manifest. It uploads the complete runnable folder. The bundle does not require a system .NET runtime or MSIX registration.

## Architecture and validation

`GitHubPackages`: approved app sources and published Windows package discovery. `PackageInstaller`: checksum verification, bounded extraction, standalone launching. `LocalStore`: serialized, atomic registry writes. `InstallUi`: install selection, included-app setup, download/cancel states. `UpdateService`: outside-app checks. `Preferences`: independent appearance storage.

`registry.json` and `appearance.json` live under `%LOCALAPPDATA%\Aquarium`. Invalid registries are preserved with writes disabled. Editing assumes one Aquarium process; cross-process coordination and signed self-updating remain future work.

```powershell
dotnet run --project .\tests\Aquarium.PackageTests\Aquarium.PackageTests.csproj -c Release
```

The tests use generated, never-executed fixture files and isolated temporary directories. They exercise package integrity, traversal/duplicate rejection, expected executables, registry persistence, failure rollback, previous-version retention and concurrent bookmarks. Check the current GitHub workflow result for actual build status.

Windows smoke checks: extract the full bundle, launch Aquarium, confirm FishTank/FishNet are registered and FishBowl is absent, open each included app, check public packages and optionally install FishBowl. Verify cancel/error recovery and persistent Library after restart. Runtime visual/playback behavior requires a real Windows desktop.

Local verification: both native apps published successfully with the supported runtime, Aquarium opened on Windows, and all 21 installer/persistence tests passed. Hosted builds may remain queued independently. See the installed bundle verification for the actual default-app registration state.


## Reference layout preview

This review branch adds a searchable app-tile catalog with a real installed-app summary, responsive layout, and matching Aquarium-family PNG/ICO assets. Installation, update management and customization remain in place. Preview builds do not replace the installed release bundle. Complete visual polish, grid/list preferences and broader accessibility checks remain follow-up work.

