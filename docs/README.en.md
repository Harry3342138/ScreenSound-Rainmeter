# ScreenSound · Rainmeter Edition

[中文](../README.md) · [Downloads](https://github.com/Harry3342138/ScreenSound-Rainmeter/releases) · [Upstream](https://github.com/twibster/ScreenSound)

A community fork of ScreenSound by Omar Omran / twibster. It keeps a player's monitor assignment when the window is minimized or hidden, and optionally connects each monitor's speaker assignment to a Rainmeter AudioLevel visualizer. Version `2.1.0-rainmeter.1` is a prerelease.

## Quick start

Requires Windows 11 x64. Install Rainmeter and your own AudioLevel skin separately.

1. Download and extract a release ZIP. Choose **self-contained** to include the .NET runtime, or **framework-dependent** if .NET 8 Desktop Runtime x64 is installed.
2. Exit any running ScreenSound instance using its tray menu.
3. Run `Install.cmd` for a per-user install, or run the extracted `ScreenSound.exe` directly.
4. Open **ScreenSound Rainmeter Edition** from Start (or double-click its tray icon). Assign speakers on **Home**.
5. Open **Settings → Configure visualizers…**. Select Rainmeter.exe, add a visualizer, choose its monitor and skin INI, then select its parent AudioLevel measure. The configuration folder is inferred from Rainmeter's skin directory; portable/custom installations may need a manual value such as `MyVisualizer\Bars`.
6. Enable synchronization and save. Changes to speaker assignments now update the corresponding loaded skins.

Use separate skin files/configurations for independent visualizers on different monitors. Synchronization does not load or reposition skins. Both editions share speaker settings and a single-instance guard; run one at a time. The edition name in the window and tray identifies the fork.

![Settings](rainmeter-settings.png)

## Behavior and performance

- Minimized/hidden players retain their last recorded monitor. Restored windows continue following their location.
- A visualizer follows the complete output of its assigned device. Two monitors assigned to one device show that same output.
- Integration uses existing mapping/device-change events: no added polling loop, file watcher, or resident helper.
- Unchanged mappings do not rewrite or refresh skins. Only affected configurations are refreshed after a change.
- An optional 33 ms limit caps fast skin updates at roughly 30 FPS, preserving slower existing update intervals.
- Unavailable/unassigned devices disable the parent measure until the mapping returns.

CPU and memory usage depend on the original applications, skin complexity, and activity; this is not a fixed resource guarantee.

## Backup and removal

The first edit creates `<skin>.ini.screensound.bak`. The **Restore skins and disable** button restores the full saved files, including replacing any later manual edits; it asks for confirmation. Disabling synchronization alone leaves the last selected device in the skin.

To uninstall, restore skins if desired, exit the app, and run `Uninstall.cmd` from `%LOCALAPPDATA%\Programs\ScreenSound-Rainmeter`. Shared audio settings, skin files, and backups are retained. A previous startup entry is restored if this installer replaced it. `Install.ps1 -StartWithWindows -DesktopShortcut` offers optional setup flags.

For portable use, disable startup first, exit, then remove the extracted directory. Release binaries are currently unsigned; SHA-256 checksums are published with each release.

## Configuration and limits

Bindings are stored in `%APPDATA%\ScreenSound\RainmeterBindings.json`, separate from audio mappings in `settings.json`. Integration is off until configured. See the [placeholder example](RainmeterBindings.example.json).

Only AudioLevel parents directly defined in the selected INI are supported; parent measures in `@Include` files and other audio plugins are not supported yet. A player first observed while already tray-hidden must be restored once to establish its screen. Re-select a monitor when display device names change; use Home's Refresh after a display topology change.

Validation currently covers one real dual-monitor Windows environment, not every Windows version, hardware configuration, or third-party skin. Windows 10 and ARM64 have not been validated.

## Build and test

Requires .NET 8 SDK:

```powershell
dotnet build ScreenSound.sln -c Release
dotnet run --project tests/MonitorRegression/MonitorRegression.csproj -c Release
dotnet run --project tests/RainmeterRegression/RainmeterRegression.csproj -c Release
pwsh -File scripts/Package.ps1 -Version 2.1.0-rainmeter.1
```

The regression suites cover 16 monitor-state cases and 22 Rainmeter cases. An additional 12-case native Win32 check runs manually on a desktop with two monitors. See [test instructions](../tests/README.md). The monitor fix is submitted separately as [upstream PR #19](https://github.com/twibster/ScreenSound/pull/19).

## Credits

ScreenSound is Copyright © 2026 Omar Omran, [MIT licensed](../LICENSE). This fork is published by [Harry3342138](https://github.com/Harry3342138) under the same license with upstream attribution retained. [Dependency notices](../THIRD_PARTY_NOTICES.md) are included. The in-app donation link still supports the original author. This is an independent community fork, not an official Rainmeter release.
