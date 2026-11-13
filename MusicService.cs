using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MinePix;

/// <summary>
/// Watches the Windows System Media Transport Controls (SMTC): whatever app is playing
/// audio reports here (Spotify, YouTube in Chrome/Edge, VLC, foobar, ...).
/// One background subscription, marshaled to the UI thread.
/// </summary>
public sealed class MusicService
{
    public static MusicService Instance { get; } = new();

    /// <summary>Raised (on the UI thread) whenever anything about the session changes.</summary>
    public event Action? Changed;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private Dispatcher? _dispatcher;

    public bool HasSession { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public string SourceApp { get; private set; } = "";
    public TimeSpan Duration { get; private set; } = TimeSpan.Zero;
    public TimeSpan Position { get; private set; } = TimeSpan.Zero;
    public bool IsPlaying { get; private set; }
    public System.Windows.Media.ImageSource? Artwork { get; private set; }

    private MusicService() { }

    private static void Log(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "music.log"),
                $"{DateTime.Now:s} {message}{Environment.NewLine}");
        }
        catch { }
    }

    public async Task InitializeAsync(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            Log("SMTC manager acquired");
            _manager.CurrentSessionChanged += (_, _) => HookSession(_manager.GetCurrentSession());
            _manager.SessionsChanged += (_, _) => HookSession(_manager.GetCurrentSession());
            HookSession(_manager.GetCurrentSession());
        }
        catch (Exception ex)
        {
            Log("SMTC init failed: " + ex);
        }
    }

    private void HookSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, _session))
        {
            _ = RefreshAsync();
            return;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionEvent;
            _session.PlaybackInfoChanged -= OnSessionEvent;
            _session.TimelinePropertiesChanged -= OnSessionEvent;
        }

        _session = session;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionEvent;
            _session.PlaybackInfoChanged += OnSessionEvent;
            _session.TimelinePropertiesChanged += OnSessionEvent;
        }

        Log(session is null ? "session: none" : $"session hooked: {session.SourceAppUserModelId}");
        _ = RefreshAsync();
    }

    private void OnSessionEvent(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = RefreshAsync();
    }

    /// <summary>Re-read everything from the current session and ping the UI thread.</summary>
    public async Task RefreshAsync()
    {
        var session = _session;
        try
        {
            if (session is null)
            {
                Apply(false, "", "", "", "", TimeSpan.Zero, TimeSpan.Zero, false, null);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var duration = timeline?.EndTime ?? TimeSpan.Zero;
            var position = timeline?.Position ?? TimeSpan.Zero;

            // artwork: stream -> memory stream -> frozen BitmapImage
            System.Windows.Media.ImageSource? art = null;
            try
            {
                if (props?.Thumbnail is not null)
                {
                    using (var thumbStream = await props.Thumbnail.OpenReadAsync())
                    {
                        var mem = new MemoryStream();
                        var buffer = new byte[8192].AsBuffer();
                        ulong total = 0;
                        while (true)
                        {
                            IBuffer chunk = await thumbStream.ReadAsync(buffer, (uint)buffer.Length, InputStreamOptions.None);
                            if (chunk.Length == 0) break;
                            total += chunk.Length;
                            mem.Write(chunk.ToArray(), 0, (int)chunk.Length);
                            if (total > 20_000_000) break; // don't slurp absurd thumbnails
                        }
                        mem.Position = 0;
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.StreamSource = mem;
                        bmp.DecodePixelWidth = 128;
                        bmp.EndInit();
                        bmp.Freeze();
                        art = bmp;
                    }
                }
            }
            catch
            {
                art = null; // some apps don't expose artwork — fine, placeholder shows
            }

            Apply(true,
                props?.Title ?? "",
                props?.Artist ?? "",
                props?.AlbumTitle ?? "",
                PrettySourceName(session.SourceAppUserModelId),
                duration, position, isPlaying, art);
        }
        catch (Exception ex)
        {
            Log("refresh failed: " + ex.Message);
            Apply(false, "", "", "", "", TimeSpan.Zero, TimeSpan.Zero, false, null);
        }
    }

    private void Apply(bool has, string title, string artist, string album, string source,
        TimeSpan duration, TimeSpan position, bool isPlaying, System.Windows.Media.ImageSource? art)
    {
        HasSession = has;
        Title = title;
        Artist = artist;
        Album = album;
        SourceApp = source;
        Duration = duration;
        Position = position;
        IsPlaying = isPlaying;
        if (art is not null) Artwork = art; // keep last artwork briefly when an event loses it

        _dispatcher?.Invoke(() => Changed?.Invoke());
    }

    public Task TogglePlayPauseAsync() => _session is null ? Task.CompletedTask : _session.TryTogglePlayPauseAsync().AsTask();
    public Task SkipNextAsync() => _session is null ? Task.CompletedTask : _session.TrySkipNextAsync().AsTask();
    public Task SkipPreviousAsync() => _session is null ? Task.CompletedTask : _session.TrySkipPreviousAsync().AsTask();

    public async Task SeekAsync(double fraction)
    {
        if (_session is null) return;
        var tl = _session.GetTimelineProperties();
        if (tl is null) return;
        var seconds = tl.EndTime.TotalSeconds;
        if (seconds <= 0) return;
        await _session.TryChangePlaybackPositionAsync((long)(fraction * seconds * 1000));
    }

    private static string PrettySourceName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        var lower = id.ToLowerInvariant();
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("msedge") || lower.Contains("microsoftedge")) return "Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("foobar")) return "foobar2000";
        if (lower.Contains("vlc")) return "VLC";
        if (lower.Contains("applemusic") || lower.Contains("apple.music")) return "Apple Music";
        if (lower.Contains("youtube")) return "YouTube";
        if (lower.Contains(".music.") || lower == "music") return "Media Player";
        // fall back: strip common suffixes/extensions for display
        var name = System.IO.Path.GetFileNameWithoutExtension(lower);
        return string.IsNullOrWhiteSpace(name) ? id : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static string FormatTime(TimeSpan t) =>
        t.Hours > 0 ? $"{t.Hours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}
