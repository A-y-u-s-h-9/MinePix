# MinePix

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

Then hover the top-center pill. Exit via the tray icon or tray → Settings → Close.

## Why another island app?

The Windows island landscape today is mostly paid and closed-source (NotchPixel, $9.99,
or unfinished ports heavy with Electron). MinePix aims to be the polished, **all-native**,
**free & open** one — your daily-driver island, without the paywall or the 600 MB Chromium.

It is inspired by iOS' Dynamic Island but is an original implementation — not a clone of any
code, assets or trademarks.

## License

MIT — see [LICENSE](LICENSE).
