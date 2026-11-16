# Packaging the MSIX (M7 prerequisite)

`Build-Msix.ps1` packages the Release build into a side-loadable `MinePix.msix` with a **development package identity** (`MinePix.App`, publisher `CN=MinePix Dev`). It uses the Windows SDK Build Tools that resolve from the NuGet cache (`D:\nuget\packages\microsoft.windows.sdk.buildtools`) — no system-wide SDK install needed.

The package needs a self-signed certificate to install, and that certificate must be trusted by the **Local Machine** store — that step requires an administrator PowerShell window.

## One-time elevated install (requires admin)

Run this from an elevated PowerShell:

```powershell
Set-Location D:\Programming\MinePix
.\packaging\Build-Msix.ps1    # build + stage + pack + sign
.\packaging\Install-Msix.ps1  # trust + install
```

Then launch & verify (also from the elevated shell, or after install):

```powershell
$env:MINEPIX_FORCE_TAB = 'notifications'
$pkg = (Get-AppxPackage MinePix.App).InstallLocation
& "$pkg\MinePix.exe"
Get-Content "$env:APPDATA\MinePix\notify.log"   # access=Allowed, "subscribed to NotificationChanged", refreshed lines
```

`NotificationService` logs to `%APPDATA%\MinePix\notify.log` on startup: access request, subscription result, and every refresh. Under the MSIX identity the live-event line must say `subscribed to NotificationChanged`; without it the service logs that live events are unavailable (0x80070490) but still serves the existing feed.

Without elevation, `Add-AppxPackage` fails with `0x800B0109` ("The root certificate of the signature in the app package or bundle must be trusted").

## Important callouts found while wiring this up

- **You do not need MSIX to *read* existing notifications.** The app (unpackaged) successfully calls `RequestAccessAsync` → `Allowed` and `GetNotificationsAsync` returns all visible toasts (OpenCode, WhatsApp, Windows Security, Storage settings, …). What needs the identity is **`NotificationChanged` events** — subscribing to live updates throws `0x80070490` unpackaged and is the whole reason the app has to be packaged for M7.
- The package is intentionally dev-only. For any real distribution the identity (`Name`, `Publisher`) and signing story need to move to real Store tooling + keys; do not commit those.
- `Build-Msix.ps1` and `Install-Msix.ps1` fetch nothing from the web and do not ship a certificate in the repo.
