# MinePix

A free, open-source **Dynamic Island for Windows 10 & 11** — an island that hangs from the
top-center of your screen, like a MacBook notch or the iPhone Dynamic Island.

Built native — **.NET 8 + WPF**, no web UI. Per-pixel alpha rendering with a liquid-glass look:
translucent tint, sheen and a soft glow, all true transparency composited by DWM.

![MinePix island with music now-playing](docs/screenshot.png)

> Status: working daily driver. Core features ship — music, clipboard, quick controls — polished and native.

## What works today

- **Dynamic-island pill** — wider capsule shape, liquid-expands on hover; toggle to the slimmer classic notch in Settings
- **Collapsed pill** — MinePix dot + live clock + (when playing) ♪ glyph + track title
- **Expanded card with icon navigation** — Music / Clipboard / Controls / Settings gear
- **Music now-playing** (SMTC) — artwork, title, artist, seek bar, play/pause/skip/next buttons, progress arc
- **Clipboard history** — SQLite store, live capture (text/files/images), search, pin/delete/clear, Enter-to-copy
- **Quick controls (iOS-style)** — tall vertical sliders for brightness & volume, Wi-Fi/BT toggles with live SSID captions, Airplane + Focus soon chips
- **Bluetooth** — tap BT tile to see paired devices with names; Wi-Fi shows connected SSID + network list with security badges
- **Settings window** — dedicated window outside the island (tray → Settings, or the gear icon in the island)
- **System tray** — show/hide the island, open settings folder, exit
- **Theme** — Graphite / Glass / Solid tints

## Roadmap

- [x] M1 — Island skeleton (tray, hover expand/collapse, settings, single instance)
- [x] M2 — Liquid glass look (themeable tint, sheen, glow)
- [x] M3 — Music now-playing (SMTC: artwork, play/pause/skip, seek, progress arc)
- [x] M4 — Clipboard history (SQLite, pin/search, copy-back on click)
- [x] M5 — Quick controls (iOS-style): two tall vertical sliders + Wi-Fi/BT captions + roulette
- [x] M6 — Quick actions (ms-screenclip / Focus mode soon)
- [ ] M7 — Notifications peek (MSIX identity required)
- [ ] M8 — Game strip (FPS / CPU / GPU / RAM over borderless games)
- [ ] M9 — Optional taskbar replacement
- [ ] M10 — Microsoft Store packaging

## Build

Requires the .NET 8 SDK:

```powershell
dotnet build
bin\Debug\net8.0-windows\MinePix.exe
```

Then hover the top-center pill. Exit via the tray icon.

## Why another island app?

The Windows island landscape today: paid closed-source ([NotchPixel](https://notchpixel.com/), $9.99),
or open source but rough/unmaintained (DynamicWin, WinIsland, eIsland — several built on Electron).
MinePix aims to be the polished, **all-native**, free & open one. It is inspired by NotchPixel and
iOS' Dynamic Island but is an original implementation — not a clone of any code, assets or trademarks.

## License

MIT — see [LICENSE](LICENSE).
