# MinePix

A free, open-source **Dynamic Island for Windows 10 & 11** — an island that hangs from the
top-center of your screen, like a MacBook notch or the iPhone Dynamic Island.

Built native — **.NET 8 + WPF**, no web UI. Per-pixel alpha rendering with a liquid-glass look:
translucent tint, sheen and a soft glow, all true transparency composited by DWM.

![MinePix island over a bright gradient](docs/screenshot.png)

> Status: early development. It currently does the island itself, and it does it well.

## What works today

- **Island window** — borderless, always-on-top, flush with the top edge of your screen,
  hover-to-expand / pointer-away-to-collapse with ease-out animations
- **Collapsed pill** — MinePix dot + live clock
- **System tray** — show/hide the island, open the settings folder, exit
- **Settings** — `%APPDATA%\MinePix\settings.json` (sizes, collapse delay, theme)
- **Liquid glass** — 100% real translucency: bright backgrounds show through the island
- **Single instance**, click-through support planned

## Roadmap

- [x] M1 — Island skeleton (tray, hover expand/collapse, settings, single instance)
- [x] M2 — Liquid glass look (themeable tint, sheen, glow)
- [x] M3 — Music now-playing (SMTC: artwork, play/pause/skip, seek, progress arc)
- [ ] M4 — Clipboard history (SQLite, pin/search, paste on click)
- [ ] M5 — Quick controls (volume, brightness, Wi-Fi, Bluetooth)
- [ ] M6 — Reminders & timer (snooze/repeat, shown in the island)
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
