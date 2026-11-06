# Aquarium

Native C# / .NET 8 / WinUI 3 Windows desktop hub with a private Aquarium-only app catalog, GitHub release installation, local Library, Downloads, Updates and customization.

## Release 0.4.2

The tagged release workflow publishes `Aquarium-complete-windows-x64.zip`, SHA-256 checksums and build metadata after native Windows publishing and package tests pass. The single-app unsigned download includes Aquarium, FishTank 0.16.0 and FishNet 0.3.1 from pinned source revisions. FishBowl 1.29.5 is released separately for Windows/Linux and remains optional in App install. Native playback, app interaction and physical display acceptance still require manual verification.

## Complete bundle

The `Aquarium-complete-windows-x64` build artifact includes Aquarium, FishTank and FishNet. Extract the ZIP and run its only file, `Aquarium.exe`. The launcher prepares the full runtime automatically under `%LOCALAPPDATA%\Aquarium\Runtime` and checks its SHA-256 file hashes before every launch. A small preparation window shows while this runs. No support files are created beside the downloaded app. Damaged runtime caches are preserved and replaced; your settings and installed apps are kept separately. Moving the launcher to another folder works without moving runtime files. Old runtime versions are retained; this release does not remove them automatically. On first launch, Aquarium verifies and installs its two included app packages into its managed app folder. A brand-new FishTank profile receives the Aquarium colors using its released custom-palette support; existing appearance and security preferences are never replaced. It never launches them automatically. FishBowl is **not** included and remains an optional install from App install.

The companion apps are built from the immutable source revisions pinned in the workflow. The bundle records their actual versions and SHA-256 hashes; it does not depend on an older downloaded preview. Windows builds are unsigned.

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

A source-only build has no Bundled folder; it still provides the App install page. The GitHub workflow publishes a complete self-contained hub, builds pinned FishNet source, downloads/verifies the inspected FishTank release, and generates its bundled-app hash manifest. It embeds the complete runtime in a self-contained single-file launcher and uploads only that app. The bundle does not require a system .NET runtime or MSIX registration.

## Architecture and validation

`GitHubPackages`: approved app sources and published Windows package discovery. `PackageInstaller`: checksum verification, bounded extraction, standalone launching. `LocalStore`: serialized, atomic registry writes. `InstallUi`: install selection, included-app setup, download/cancel states. `UpdateService`: outside-app checks. `Preferences`: independent appearance storage.

`registry.json` and `appearance.json` live under `%LOCALAPPDATA%\Aquarium`. Invalid registries are preserved with writes disabled. Editing assumes one Aquarium process; cross-process coordination and signed self-updating remain future work.

```powershell
dotnet run --project .\tests\Aquarium.PackageTests\Aquarium.PackageTests.csproj -c Release
```

The tests use generated, never-executed fixture files and isolated temporary directories. They exercise package integrity, traversal/duplicate rejection, expected executables, registry persistence, failure rollback, previous-version retention and concurrent bookmarks. Check the current GitHub workflow result for actual build status.

Windows smoke checks: extract the single-app bundle, launch Aquarium, confirm FishTank/FishNet are registered and FishBowl is absent, open each included app, check public packages and optionally install FishBowl. Verify cancel/error recovery and persistent Library after restart. Runtime visual/playback behavior requires a real Windows desktop.

CI checks cold and repeat launcher startup, launch after moving the file, runtime integrity and bundled-app availability. Visual interaction and physical-device acceptance remain manual checks.


## Updating Aquarium

Open **Updates → Check for Aquarium update**. After you approve an available release, Aquarium downloads the Windows package from this repository, checks its SHA-256 digest, and stages the new launcher in your local profile. Close Aquarium and reopen it from the updated desktop or Start menu shortcut. Your app library and settings stay in your profile. The previous launcher is retained.

Use **Updates → Repair Aquarium shortcuts** to create or refresh the desktop and Start menu shortcuts for the current launcher. The launcher and runtime executable carry the Aquarium icon in their Windows file resources.

To make a source change available through the updater, raise the runtime and launcher project versions together, then tag that commit with the matching version (for example, v0.4.4). Publish the draft produced by the Windows release workflow after verification. Git commits alone are not installable updates.

### Uninstalling a managed app

Choose **Uninstall** on an installed app in **Library** or **App install**. Close the app first. Aquarium removes its managed program versions and keeps the app profile and library bookmark. An explicitly uninstalled bundled app stays removed on restart. Use **Install included version** or a published package in **App install** to reinstall it.

### Previewing appearance

Open **Settings** to preview colors, title bar, frame, card corners, heading size, sidebar width, and spacing. **Apply appearance** saves your choices. Leaving Settings discards unapplied choices. Library cards display the installed executable's Windows icon when available.
