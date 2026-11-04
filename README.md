# Aquarium

A native C# / .NET 8 / WinUI 3 Windows desktop shell for a private Aquarium-only app collection. Dark aquatic surfaces, teal accents, responsive scrolling, native keyboard navigation, and six destinations: Home, Store, Library, Downloads, Updates, Settings.

## Build and run

Prerequisites: Windows 10 version 2004 (build 19041) or newer, x64; .NET 8 SDK; Visual Studio 2022 17.8+ with Windows application development / WinUI C# tooling and Windows SDK 10.0.26100. Restore requires access to NuGet. The pinned Windows App SDK is 1.6.250205002. This is a reproducible starting baseline, not a claim that it is the latest release.

From this directory in PowerShell:

```powershell
dotnet restore .\src\Aquarium\Aquarium.csproj
dotnet build .\src\Aquarium\Aquarium.csproj -c Debug -p:Platform=x64
dotnet run --project .\src\Aquarium\Aquarium.csproj -c Debug -p:Platform=x64
```

Alternatively open `src/Aquarium/Aquarium.csproj` in Visual Studio, select x64, and run. This is an unpackaged desktop app; no MSIX registration is needed. Both .NET and Windows App SDK runtimes are configured as self-contained.

To produce a runnable folder:

```powershell
dotnet publish .\src\Aquarium\Aquarium.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o .\publish
.\publish\Aquarium.exe
```

Keep the entire publish folder together, including `Data/catalog.json` and runtime files. This does not create an installer. See [Microsoft's unpackaged WinUI documentation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app?tabs=csharp).

## Working behavior

- Home presents the collection and links to Store.
- Store filters the three local catalog entries by name/category and opens details.
- Save/remove library bookmarks persist across restarts in `%LOCALAPPDATA%\Aquarium\registry.json`.
- Library displays saved entries, clearly separated from installed apps.
- Downloads and Updates show honest unavailable states. Settings explains storage and integration status.
- Read errors preserve the registry and disable writes. Failed saves report errors without committing the in-memory change. Writes use a temporary sibling followed by replacement.

No install, launch, update check, scan, emulator, or media playback is simulated. No private VPN, network management, or Tailscale projects or functionality are included. No existing apps were available in this workspace, inspected, or modified.

## Architecture

```text
WinUI shell (MainWindow)
    └── LocalStore
         ├── bundled Data/catalog.json (private allowlist)
         ├── LocalRegistry (bookmarks + future installation records)
         └── IAquariumModule adapters
              ├── security: pending
              ├── emulation: pending
              └── media: planned
IPackageInstaller → explicit unavailable implementation
```

`Models.cs` contains catalog records, the versioned registry, module contract, and installation seam. `LocalStore.cs` handles data loading and persistence. `MainWindow` owns shell rendering and navigation. Small code-built native panels keep the initial project straightforward; move destination content into separate pages/view models as behavior expands.

The bundled catalog is the sole source of entries; users cannot add arbitrary apps through the UI. "Private" describes this restricted product scope: it is not an authentication or package trust boundary. The catalog is editable by someone with filesystem access. Do not treat it as permission to run code. There is no remote marketplace backend.

## Integrating existing projects

1. Inspect each actual project, its framework, output artifacts, startup arguments, lifecycle, licenses, and privilege requirements before making changes.
2. Preserve standalone operation. Prefer an out-of-process adapter implementing `IAquariumModule`; do not load unreviewed assemblies into the shell.
3. Specify an Aquarium package manifest with stable app ID, version, architecture, entry point, minimum shell version, required permissions, and signed artifact digest.
4. Build a verified local package importer behind `IPackageInstaller`, including staging, integrity/signature verification, path containment, rollback, and uninstall behavior before enabling Install.
5. Record `InstalledApp` only after successful installation. Resolve launch paths from verified installation records and validate them before starting processes. No automatic launch on catalog load.
6. Implement real job state, cancellation, and failures before exposing download progress. Implement approved release sources and version comparison before update claims. Reserve Media until its app exists.

Security and Emulation entries are integration plans, not representations of inspected packages. Their names and summaries can be adjusted once source projects are supplied.

## Verification

The authoring machine had no discoverable .NET SDK or Visual Studio installation. Compilation, launch, and visual validation have **not** been performed. Source XML/JSON and project wiring were checked statically. Run the build above on a configured Windows development machine before relying on this scaffold.

Manual smoke checks after building:

1. Open all six destinations; resize the window and navigate with the keyboard.
2. Search `Security`, search a category, then a nonexistent value; verify the empty state.
3. Open details. Save an app, visit Library, restart, and verify persistence; remove and restart again.
4. Confirm no Installed/Launch action or fabricated download/update status appears.
5. With the app closed, back up the registry, replace it with invalid JSON, reopen, and verify a warning and disabled library writes; restore the backup with the app closed.

The registry is intended for one running instance. Cross-process write coordination, schema migrations, actual installation, and automated UI testing are future work.
