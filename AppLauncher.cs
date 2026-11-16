using System.Diagnostics;

namespace MinePix;

/// <summary>
/// Activates an installed app by its Application User Model ID (AUMID) — the same
/// identity that surfaces on a notification's <c>AppInfo</c>. Uses the shell's
/// <c>shell:AppsFolder\AUMID</c> resolution, which works for packaged (MSIX) apps and
/// registered win32 apps without a COM registration of
/// <c>ApplicationActivationManager</c> (that CLSID can be absent).
/// </summary>
internal static class AppLauncher
{
        // Known notification-source AUMIDs that don't resolve in AppsFolder directly:
        // map each to the AppsFolder entry the shell actually resolves.
        private static readonly System.Collections.Generic.Dictionary<string, string> KnownFallbacks =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Windows.Defender.SecurityCenter"] = "Microsoft.SecHealthUI_8wekyb3d8bbwe!SecHealthUI",
            };

        /// <summary>
        /// Launches (or focuses) the app identified by <paramref name="aumid"/>.
        /// Returns true on success; on failure <paramref name="error"/> carries the reason.
        /// </summary>
        public static bool TryActivate(string aumid, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(aumid))
            {
                error = "no app id on this notification";
                return false;
            }

            if (TryShellOpen(aumid, out error))
                return true;

            if (KnownFallbacks.TryGetValue(aumid, out var fallback) && TryShellOpen(fallback, out error))
                return true;

            return false;
        }

        private static bool TryShellOpen(string aumidOrFallback, out string error)
        {
            error = "";
            try
            {
                var psi = new ProcessStartInfo($"shell:AppsFolder\\{aumidOrFallback}")
                {
                    UseShellExecute = true,
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
}
