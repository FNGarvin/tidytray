# TidyTray

A small Windows tray watchdog that keeps your notification area icons exactly as visible or hidden as you set them — even after the owning app updates.

## The problem

Windows defaults every tray icon to hidden, and treats "shown" as a fragile, temporary favor it's free to revoke. Explicitly tell it to show an icon and it'll usually honor that — until the owning app updates, at which point Windows silently reverts the icon to hidden with zero notification that it happened. You just eventually notice the icon is gone.

The mechanism behind it: Windows remembers each icon's visibility in the registry, keyed roughly by the icon's owning executable path. Any app whose install path changes on every update trips this — which, in practice, means most of them. The Xbox app is a good example everyone's probably got installed: it's a Microsoft Store (MSIX) package, and the entire package version is baked into its install path:

```
...\WindowsApps\Microsoft.GamingApp_2608.1001.17.0_x64__8wekyb3d8bbwe\XboxPcTray.exe
...\WindowsApps\Microsoft.GamingApp_2609.1002.4.0_x64__8wekyb3d8bbwe\XboxPcTray.exe
```

Electron/Squirrel-style auto-updaters (Discord, Slack, Claude Desktop, and plenty of others) have the same problem via a different pattern — each new version installs into its own `app-x.y.z` folder. Either way, every update changes the path, Windows treats the icon as brand new, and it silently defaults back to hidden — discarding a preference you explicitly set, with no notification that it happened.

## What TidyTray does

TidyTray runs quietly in the tray and:

1. Watches `HKCU\Control Panel\NotifyIconSettings` (Explorer's own undocumented store for this) for changes, live — no polling delay, no Explorer restart needed.
2. Resolves each icon to a stable, human-readable identity (the exe's product/file description, falling back to tooltip or filename) so an app update doesn't fork your preference into a new entry.
3. Automatically detects and separates genuinely distinct icons that happen to share a generic name (a number of Windows' own system tray icons all report the identical `ProductName`, for example) — without splitting apart different versions of the same real app.
4. Re-applies your stored preference the moment it drifts, and remembers a sensible default (visible) the first time it ever sees a new icon.
5. Gives you a simple checklist UI, opened from the tray icon, to set the preference for anything it has ever seen.

## Requirements

Windows 10/11. No .NET runtime installation needed — release builds are self-contained, single portable `.exe` files.

## Installing

Download the latest `TidyTray-*-win-x64.exe` from the [Releases](https://github.com/FNGarvin/tidytray/releases) page and run it — no installer, no admin rights, nothing else to set up.

## Running

Just run `TidyTray.exe`. It sits in the tray with no visible window. Double-click (or right-click → Settings) to open the checklist. Right-click → "Start with Windows" registers it to launch automatically at login (off by default — nothing is added to your startup sequence unless you turn it on yourself).

## Building from source

For development:

```
cd src/TidyTray
dotnet build
```

Releases are built and published automatically — pushing a `v*` tag triggers [GitHub Actions](.github/workflows/release.yml) to produce the portable single-file exe and attach it to a GitHub release, so there's normally no need to do this by hand. To reproduce that build locally anyway:

```
cd src/TidyTray
dotnet publish -c Release
```

Output lands in `src/TidyTray/bin/Release/net10.0-windows/win-x64/publish/TidyTray.exe` — a single self-contained executable, no installer required.

## How the underlying mechanism works

Writing a `DWORD` value named `IsPromoted` (`1` = shown, `0`/absent = hidden-in-overflow) to the relevant subkey under `HKCU\Control Panel\NotifyIconSettings` takes effect live, in both directions, confirmed by hand against both native and Electron-based apps — no Explorer restart required. This is undocumented behavior (there's no public Win32 API for it), so it's possible a future Windows build changes how it works.

## License

MIT — see [LICENSE](LICENSE).
