using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace MinePix;

/// <summary>Row view-model bound into the notifications tab.</summary>
public sealed class NotificationItem
{
    public uint Id { get; init; }
    public string Aumid { get; init; } = "";
    public string AppName { get; init; } = "";
    public string Text { get; init; } = "";
    public string MetaLine { get; init; } = "";
    public string TimeLine { get; init; } = "";
    public string Glyph { get; init; } = "\U0001F514";
    // The WinRT notification API does not expose per-item read state; anything still
    // in the notification center is treated as unread (cleared toasts disappear).
    public bool Unread => true;

    public NotificationItem(UserNotification n)
    {
        Id = n.Id;
        Aumid = n.AppInfo?.AppUserModelId?.Trim() ?? "";
        AppName = n.AppInfo?.DisplayInfo?.DisplayName?.Trim() ?? "Unknown app";
        TimeLine = n.CreationTime.ToString("HH:mm");
        MetaLine = $"{AppName} · {n.CreationTime.ToLocalTime():MMM d HH:mm}";

        var text = TryExtractText(n);
        Text = string.IsNullOrWhiteSpace(text) ? AppName : text;
    }

    private static string TryExtractText(UserNotification n)
    {
        try
        {
            // Most toast templates are "ToastGeneric". Fall back to the first non-empty text in any binding.
            var binding = n.Notification?.Visual?.GetBinding("ToastGeneric")
                          ?? n.Notification?.Visual?.Bindings?.FirstOrDefault();
            if (binding is null)
                return "";

            var chunks = binding.GetTextElements()
                .Select(t => t.Text)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            return chunks.Count > 0 ? string.Join(" — ", chunks) : "";
        }
        catch
        {
            return "";
        }
    }
}

/// <summary>
/// Reads the Windows notification center (toast notifications). Works unpackaged for reads;
/// live <c>NotificationChanged</c> events require the MSIX identity from <c>packaging/</c>.
/// One singleton, marshaled to the UI thread via the Dispatcher given at Initialize.
/// </summary>
public sealed class NotificationService
{
    public static NotificationService Instance { get; } = new();

    public event Action? Changed;

    private NotificationService() { }

    public ObservableCollection<NotificationItem> Items { get; } = new();
    public int UnreadCount { get; private set; }
    public bool HasNotifications => Items.Count > 0;

    private Dispatcher? _dispatcher;
    private bool _subscribed;
    private UserNotificationListener? _listener;

    /// <summary>Dev/ops logging into the same notify.log the service uses.</summary>
    public void LogNote(string message) => Log(message);

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "notify.log"), $"{DateTime.Now:o} {message}{Environment.NewLine}");
        }
        catch { }
    }

    public async Task InitializeAsync(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            Log($"access={access}");
        }
        catch (Exception ex)
        {
            Log("access request failed: " + ex.Message);
            return;
        }

        // the feed itself works unpackaged; only live NotificationChanged events need the MSIX identity
        await ReloadAsync();

        if (_subscribed)
            return;
        try
        {
            _listener!.NotificationChanged += (_, _) => _ = ReloadAsync();
            _subscribed = true;
            Log("subscribed to NotificationChanged");
        }
        catch (Exception ex)
        {
            // expected when the app runs without the package identity (0x80070490)
            Log("live-event subscription unavailable (needs MSIX identity): " + ex.Message);
        }
    }

    public async Task ReloadAsync()
    {
        var listener = _listener;
        if (listener is null)
            return;
        try
        {
            var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var items = list
                .OrderByDescending(n => n.CreationTime)
                .Take(20)
                .Select(n => new NotificationItem(n))
                .ToList();

            var ui = _dispatcher;
            if (ui is null)
            {
                Log("refresh skipped: no dispatcher yet");
                return;
            }

            _ = ui.BeginInvoke(() =>
            {
                Items.Clear();
                foreach (var item in items)
                    Items.Add(item);
                UnreadCount = items.Count(n => n.Unread);
                Changed?.Invoke();
            });

            Log($"refreshed: {items.Count} toast(s), unread={items.Count(n => n.Unread)}");
        }
        catch (Exception ex)
        {
            Log("refresh failed: " + ex);
        }
    }

    /// <summary>Removes a single toast from the notification center.</summary>
    public async Task DismissAsync(uint id)
    {
        var listener = _listener;
        if (listener is null)
            return;
        try
        {
            listener.RemoveNotification(id);
            Log($"removed notification id={id}");
        }
        catch (Exception ex)
        {
            Log($"remove id={id} failed: {ex.Message}");
        }
        await ReloadAsync();
    }

    /// <summary>Removes every toast we can currently see from the notification center.</summary>
    public async Task DismissAllAsync()
    {
        var listener = _listener;
        if (listener is null)
            return;
        // snapshot the current ids so removals don't mutate the enumeration
        var ids = Items.Select(i => i.Id).ToList();
        int removed = 0;
        foreach (var id in ids)
        {
            try
            {
                listener.RemoveNotification(id);
                removed++;
            }
            catch (Exception ex)
            {
                Log($"remove id={id} failed: {ex.Message}");
            }
        }
        Log($"dismissed {removed}/{ids.Count} toast(s)");
        await ReloadAsync();
    }
}
