# MinePix — v1.0.0

A free, open-source **Dynamic Island for Windows 10 & 11** — an island that hangs from the
top-center of your screen, like a MacBook notch or the iPhone Dynamic Island.

Built native — **.NET 8 + WPF**, no web UI. Per-pixel alpha rendering with a liquid-glass look:
translucent tint, sheen and a soft glow, all true transparency composited by DWM.

![MinePix collapsed pill over a wallpaper](docs/hero-pill.png)

> Day-to-day driver. Core features ship — music, clipboard, quick controls — pixel-perfect, native, and yours to keep improving.

---

## Music now-playing

![Music tab with album art, transport controls and seek bar over wallpaper](docs/hero-music.png)

## Clipboard

![Clipboard history with pinned items, search, and clear controls](docs/hero-clipboard.png)

## Quick Controls

![iOS-style brightness and volume sliders, Wi-Fi and Bluetooth tiles](docs/hero-controls.png)

---

## What works today

- **Dynamic-island pill** — wider capsule, liquid-expands on hover; toggle to slimmer classic pill in Settings
- **Collapsed pill** — MinePix dot + live clock + (when playing) ♪ + track title
- **Expanded card with icon navigation** — Music / Clipboard / Controls / Settings gear
- **Music now-playing** (SMTC) — artwork, title, artist, seek bar, transport controls, progress arc
- **Clipboard history** — local SQLite store, live capture (text/files/images), search, pin/delete/clear, copy-back on click
- **Quick controls (iOS-style)** — tall vertical sliders (brightness, volume), Wi-Fi/Bluetooth with live SSID captions, Airplane + Focus soon chips
- **Bluetooth** — tap BT tile to see paired devices list with names; Wi-Fi shows connected SSID + network list with security badges
- **Settings window** — dedicated window outside the island (tray → Settings, or the gear icon in the pill):
  theme, island mode, Music-in-pill, Start-with-Windows, widths, delays
- **System tray** — show/hide the island, open settings folder, exit
- **Theme** — Graphite / Glass / Solid tints
- **Single instance**, open source, MIT

## Roadmap

- [x] M1 — Island skeleton (tray, hover expand/collapse, settings, single instance)
- [x] M2 — Liquid glass look (themeable tint, sheen, glow)
- [x] M3 — Music now-playing (SMTC: artwork, play/pause/skip, seek, progress arc)
- [x] M4 — Clipboard history (SQLite, pin/search, copy-back on click)
- [x] M5 — Quick controls (iOS-style): two tall vertical sliders + Wi-Fi/BT captions
- [x] M6 — Quick actions (ms-screenclip shortcuts, Focus mode soon chips)
- [x] M7 — Notifications peek (bell tab + header attention chip, MSIX identity for live events)
- [x] M8 — Game strip (FPS / CPU / GPU / VRAM + temp / power over borderless games)
- [ ] M9 — v1.0.0: Optional taskbar replacement

[Download v1.0.0](https://github.com/A-y-u-s-h-9/MinePix/releases/tag/v1.0.0) — built from tag `v1.0.0`.

## Install

Two ways to run MinePix, both from the repo:

### Option A — Setup.exe (simplest)

Run **`MinePixSetup-1.0.0.exe`** (\`packaging/out/MinePixSetup-1.0.0.exe\` in a local build, or from the Releases page).
It offers **current user** (%LocalAppData%, no admin) or **all users** (Program Files, elevated) at install time,
creates a Start Menu shortcut, and can register autostart (default on; desktop shortcut is opt-in). Runs on
Windows 10 & 11, x64.

**Unsigned** — SmartScreen will warn on first launch: “More info → Run anyway”. That's the price of free & open.

> Note: the unpackaged app shows the notification feed but cannot subscribe to **live notification-center pushes**
> (that capability requires a package identity). Everything else works.

### Option B — MSIX (live notifications)

Live notification events (the island reacting the instant a toast arrives) require the MSIX identity:

```powershell
.\packaging\Build-Msix.ps1     # build + stage + pack + sign
.\packaging\Install-Msix.ps1   # trust the dev cert (once, elevated) + side-load
```

Same features, plus live notifications. The MSIX is dev-signed (`CN=MinePix Dev`) for local testing — a Store
identity replaces it for public distribution.

### Uninstall

Setup.exe → Settings → Apps → MinePix → Uninstall (also removes the autostart entry). For the MSIX: `Get-AppxPackage MinePix.App | Remove-AppxPackage`.

## Build

Requires the .NET 8 SDK:

```powershell
dotnet build
bin\Debug\net8.0-windows10.0.22621.0\MinePix.exe
```

Packaging (optional): see `packaging\README.md` for the MSIX, and `packaging\MinePix.iss` for the installer
(compile with Inno Setup 6: `ISCC.exe packaging\MinePix.iss`).

Then hover the top-center pill. Exit via the tray icon or tray → Settings → Close.

## Why another island app?

The Windows island landscape today is mostly paid and closed-source (NotchPixel, $9.99,
or unfinished ports heavy with Electron). MinePix aims to be the polished, **all-native**,
**free & open** one — your daily-driver island, without the paywall or the 600 MB Chromium.

It is inspired by iOS' Dynamic Island but is an original implementation — not a clone of any
code, assets or trademarks.

## License

MIT — see [LICENSE](LICENSE).
