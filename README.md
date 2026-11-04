# Aquarium

Native C#/.NET 8/WinUI 3 Windows desktop hub with a private Aquarium-only catalog, app installation, hosted workspaces, Library, Downloads, Updates and saved customization. FishTank is security, FishBowl is emulation, and FishNet is media. No private VPN/network/Tailscale projects are catalog entries.

## Open an app inside Aquarium

Choose **Open inside Aquarium** on an installed app's catalog tile. Its main workspace fills Aquarium's content area. Aquarium's sidebar and the workspace's **Home** button remain outside the app. Home returns to the app page and keeps the process and its current state running. **Resume app** returns to that same session. **Close app** asks the app to close normally.

Aquarium hosts only approved, verified installation paths and main windows explicitly marked by the exact process it started. A build must declare Aquarium workspace protocol 1 before Aquarium launches it. Older public releases without hosting support are rejected before a separate app is started. App install shows compatibility. Separately opened copies are not adopted or replaced.

Each app runs independently as the current Windows user; hosting is a presentation boundary, not a security sandbox. File pickers, sign-in, permission and other dialogs can still appear separately. Aquarium never captures an unmarked sign-in/startup window or bypasses an app's authentication. Matching DPI modes are required. Mixed monitors, full screen, emulator rendering, elevated windows and all dialogs still need compatibility testing.

Closing Aquarium asks each owned app to close. If an app refuses, has an unfinished dialog or is still signing in, Aquarium remains open. Processes are never force-terminated by the host. Save work normally: an unexpected shell/Windows failure can still affect hosted windows. Standalone app launch remains supported by each app itself.

## Build and run

On Windows with the .NET 8 SDK and Windows build tools:

```powershell
dotnet publish .\src\Aquarium\Aquarium.csproj -c Release -p:Platform=x64 -o .\artifacts\Aquarium
.\artifacts\Aquarium\Aquarium.exe
```

Keep the complete publish directory, including resource PRI/runtime/assets. The app is unpackaged and self-contained; it does not require MSIX registration. A source-only build has no included app packages.

The complete GitHub artifact builds pinned hosting-compatible FishNet and FishTank source and bundles their full program folders. FishBowl remains optional. To assemble the same bundle locally, build/publish each compatible app and run:

```powershell
.\scripts\Bundle-Apps.ps1 -PublishDirectory .\artifacts\Aquarium -FishNetDirectory .\artifacts\FishNet -FishTankDirectory .\artifacts\FishTank
```

The bundle checksum-verifies and installs FishTank/FishNet once on first run, skipping already-registered apps. It does not launch them automatically or replace existing appearance/security preferences. These are unsigned review builds, not a published production release.

## Install, update and recover

App install reads public releases from KailanBess/FishTank, KailanBess/FishNet and KailanBess/FishBowl only. Draft releases are excluded. FishTank permits previews; the others use stable releases. Review source/version/size before installation. A GitHub asset SHA-256 digest or matching SHA256SUMS.txt is required. Missing releases/checksums/compatible builds are reported honestly. CI artifacts are not treated as public releases.

Managed app folders are versioned under %LOCALAPPDATA%\Aquarium\Apps. Downloads shows real progress, cancellation, errors and retry. Failed installs keep the previous registration; successful ones retain old folders. App install offers rollback to tracked retained program versions. Rollback does not downgrade user data or reverify retained binaries; close the app first and review data compatibility. Untracked old folders are not adopted automatically.

Updates offers optional hourly/daily/weekly checks while Aquarium is open, plain-text release notes for all four repos and original WinGet outside-app results. Checks never install or accept source agreements. Outside updates require an explicitly reviewed exact package ID. WinGet coverage is limited to matched apps; no universal update claim is made. Vendor self-updaters and legacy standalone app update tools are not disabled by the shell.

## Data and architecture

Registry/bookmarks and appearance live under %LOCALAPPDATA%\Aquarium. Invalid registries are preserved with writes disabled. Media, saves, accounts and security profiles stay in each app's normal location. Use one Aquarium instance at a time.

- GitHubPackages: approved sources and published package discovery.
- PackageInstaller: checksum verification, bounded extraction and contained executable paths.
- WorkspaceCapability: read-only assembly metadata capability check before launch; no code is loaded to inspect it.
- HostedAppSession: exact-process marked-window discovery, native child parenting, DPI validation, resize, hide/resume and graceful close.
- WorkspaceUi: Home/workspace navigation and close handling.
- LocalStore: serialized atomic registry writes and retained-version history.
- UpdateService/UpdateSchedule: user-requested and opt-in scheduled checks.

## Checks

```powershell
dotnet run --project .\tests\Aquarium.PackageTests -c Release
dotnet run --project .\tests\Aquarium.HostingTests -c Release
```

Package checks use generated files and never run fixture executables. Hosting checks run only their own generated fixture process: capability detection, exact PID/window marker, native child styles, bounds/resize, Home hide, same-session resume, close refusal and graceful close, and refusal of unmarked startup windows. Live FishNet hosting, Converter navigation, picker open/cancel, Home/Resume with the same process and normal close were checked. Full FishTank/FishBowl interaction, security prompts, physical emulators/controllers and mixed-DPI/full-screen behavior remain manual release checks.
