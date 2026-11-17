using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace MinePix;

/// <summary>
/// Watches the foreground window: when a fullscreen app (borderless or exclusive) is the
/// foreground process — and it isn't on the exclusion list — the island shows the game strip.
/// Also samples per-process CPU, per-process GPU 3D % (perf counters), GPU memory
/// (perf counters + NVML on NVIDIA) and live FPS via ETW present events (user mode works;
/// elevated sessions not required for these providers on Windows 11).
/// One singleton, marshaled to the UI thread via the Dispatcher given at Initialize.
/// </summary>
public sealed class GameService
{
    public static GameService Instance { get; } = new();

    public event Action? Changed;

    private GameService() { }

    private Dispatcher? _dispatcher;
    private Settings? _settings;
    private DispatcherTimer? _timer;
    private int _ourPid = Environment.ProcessId;

    // target tracking
    private int _pid;
    private string _processName = "";
    private int _session;         // generation id for detection state; used to reset min/max
    private Process? _targetProcess;
    private TimeSpan _lastCpuTotal;
    private DateTime _lastCpuSample = DateTime.UtcNow;
    private double _cpuPercent;

    // ETW / FPS
    private TraceEventSession? _etw;
    private readonly ConcurrentQueue<DateTime> _presents = new();
    private bool _etwBroken;

    // ---------- public surface ----------

    public bool GameModeOverride { get; private set; }

    /// <summary>True when the foreground window currently looks like a game (or the user forced it).</summary>
    public bool GameModeActive => (IsDetected || GameModeOverride) && ProcessId != 0 && _settings?.GameStripEnabled != false;

    public bool IsDetected { get; private set; }
    public string ProcessName => _processName.Replace(".exe", "");
    public int ProcessId => _pid;
    public int Session => _session;

    public string AdapterCaption { get; private set; } = "";
    public bool IsNvidiaAdapter { get; private set; }

    /// <summary>Frames per second, 1 s rolling average. null when ETW is unavailable.</summary>
    public double? Fps { get; private set; }
    public double CpuPercent => _cpuPercent;
    public double Gpu3DPercent { get; private set; }
    public double VramUsedMb { get; private set; }
    public double AdapterVramTotalMb { get; private set; }
    public double? GpuTempC { get; private set; }
    public double? GpuPowerW { get; private set; }

    public void Initialize(Settings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            // do the expensive sampling (PDH wildcard, process reads) off the UI thread
            Task.Run(() =>
            {
                try
                {
                    EvaluateDetection();
                    SampleCpu();
                    SampleGpuAndMemory();
                    PruneFpsWindow();
                }
                catch (Exception ex)
                {
                    Log("tick failed: " + ex);
                }
                NotifyUi();
            });
        };
        _timer.Start();
        Task.Run(() => { EvaluateDetection(); SampleCpu(); SampleGpuAndMemory(); PruneFpsWindow(); NotifyUi(); });
    }

    /// <summary>Route every Changed notification onto the UI thread (or inline if the dispatcher is gone).</summary>
    private void NotifyUi()
    {
        var d = _dispatcher;
        try
        {
            if (d != null && !d.HasShutdownStarted && !d.HasShutdownFinished)
            {
                _ = d.BeginInvoke(new Action(() => Changed?.Invoke()), DispatcherPriority.DataBind);
                return;
            }
            Changed?.Invoke();
        }
        catch { }
    }

    /// <summary>Toggle the user's "game mode" override (force current foreground app to count as a game).</summary>
    public void ToggleOverride()
    {
        GameModeOverride = !GameModeOverride;
        EvaluateDetection();
        NotifyUi();
    }

    /// <summary>Rearm the FPS ETW session (used by the 'tap to retry' affordance).</summary>
    public void RearmEtw()
    {
        if (!GameModeActive)
            return;
        StopEtw();
        _etwBroken = false;
        if (_settings?.GameShowFps != false)
            StartEtw(_pid);
        NotifyUi();
    }

    /// <summary>Dev/test hook: force override on and (optionally) pin a specific process as the target.</summary>
    public void ForceGameForTesting(int? pid)
    {
        GameModeOverride = true;
        _forcedGameOnly = true;
        if (pid is int p)
        {
            try
            {
                var pr = Process.GetProcessById(p);
                IsDetected = true;
                _pid = p;
                _processName = pr.ProcessName + ".exe";
                _session++;
                try { _targetProcess?.Dispose(); } catch { }
                _targetProcess = pr;
                _lastCpuTotal = pr.TotalProcessorTime;
                _lastCpuSample = DateTime.UtcNow;
                AdapterCaption = "";
                IsAdapterVramSet = false;
                _ = PerfCounters.Max3DPercentForPid(p, out string luid);
                if (!string.IsNullOrEmpty(luid))
                    TryRefreshAdapterCaption(luid);
                StopEtw();
                _etwBroken = false;
                if (_settings?.GameShowFps != false)
                    StartEtw(p);
            }
            catch (Exception ex)
            {
                Log("FORCE_GAME failed: " + ex.Message);
            }
        }
        else
        {
            EvaluateDetection();
        }
        NotifyUi();
    }

    // ---------- detection ----------

    private const int MonitorDefaultToNearest = 2;
    private const int Tolerance = 2;
    private bool _forcedGameOnly; // set by ForceGameForTesting: do not let foreground polling clear the forced target

    private void EvaluateDetection()
    {
        if (_forcedGameOnly)
            return; // keep the forced target pinned for testing

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            ApplyDetection(null);
            return;
        }

        GetWindowThreadProcessId(hwnd, out uint pidU);
        int pid = (int)pidU;
        if (pid == 0 || pid == _ourPid)
        {
            ApplyDetection(null);
            return;
        }

        // fullscreen? window rect == monitor rect
        GetWindowRect(hwnd, out WinRect wr);
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var mi = new MonitorInfo { CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MonitorInfo)) };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref mi))
        {
            ApplyDetection(null);
            return;
        }
        bool fullscreen =
            Math.Abs(wr.Left - mi.Monitor.Left) <= Tolerance &&
            Math.Abs(wr.Top - mi.Monitor.Top) <= Tolerance &&
            Math.Abs(wr.Right - mi.Monitor.Right) <= Tolerance &&
            Math.Abs(wr.Bottom - mi.Monitor.Bottom) <= Tolerance;

        string exe;
        try
        {
            using var p = Process.GetProcessById(pid);
            exe = p.ProcessName + ".exe";
        }
        catch
        {
            ApplyDetection(null);
            return;
        }

        var settings = _settings;
        var exclusions = settings?.GameExclusions ?? DefaultExclusions;
        var gameList = settings?.GameProcessNames ?? new List<string>();

        bool excluded = exclusions.Any(e => string.Equals(e.Trim(), exe, StringComparison.OrdinalIgnoreCase));
        bool listed = gameList.Any(e => string.Equals(e.Trim(), exe, StringComparison.OrdinalIgnoreCase));

        bool isGame = listed || (fullscreen && !excluded && !IsShellClass(GetClassNameOf(hwnd))) ||
                      (GameModeOverride && fullscreen && !IsShellClass(GetClassNameOf(hwnd)));

        ApplyDetection(isGame ? (pid, exe) : ((int pid, string exe)?)null);
    }

    private static string GetClassNameOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool IsShellClass(string cls)
        => cls.Equals("Progman", StringComparison.OrdinalIgnoreCase)      // desktop
        || cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase)     // desktop alt
        || cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) // taskbar
        || cls.Equals("Button", StringComparison.OrdinalIgnoreCase)      // start? (loose)
        || cls.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase); // explorer

    private void ApplyDetection((int pid, string exe)? game)
    {
        bool wasDetected = IsDetected;
        int prevPid = _pid;

        if (game is null)
        {
            IsDetected = false;
            _pid = 0;
            _processName = "";
            _targetProcess?.Dispose();
            _targetProcess = null;
            StopEtw();
            Fps = null;
            Gpu3DPercent = 0;
            VramUsedMb = 0;
            GpuTempC = null;
            GpuPowerW = null;
            _cpuPercent = 0;
            AdapterCaption = "";
            IsNvidiaAdapter = false;
            IsAdapterVramSet = false;
            if (wasDetected || prevPid != 0)
                NotifyUi();
            return;
        }

        string exe = game.Value.exe;
        int pid = game.Value.pid;

        if (!wasDetected || prevPid != pid || !string.Equals(_processName, exe, StringComparison.OrdinalIgnoreCase))
        {
            _session++;
            _processName = exe;
            _pid = pid;
            IsDetected = true;
            _targetProcess?.Dispose();
            _targetProcess = null;
            try { _targetProcess = Process.GetProcessById(pid); } catch { _targetProcess = null; }
            _lastCpuTotal = _targetProcess?.TotalProcessorTime ?? TimeSpan.Zero;
            _lastCpuSample = DateTime.UtcNow;

            // resolve adapter on fresh detection
            AdapterCaption = "";
            IsNvidiaAdapter = false;
            var (caption, isNv, totalMb) = AdapterInfoForDisplayAndDetection();
            AdapterCaption = caption;
            IsNvidiaAdapter = isNv;
            AdapterVramTotalMb = totalMb;
            IsAdapterVramSet = totalMb > 0;

            StopEtw();
            _etwBroken = false;
            if (_settings?.GameShowFps != false && _settings.GameStripEnabled)
                StartEtw(pid);
        }
        else
        {
            IsDetected = true;
            _pid = pid;
        }

        if (!wasDetected)
            NotifyUi();
    }

    private bool IsAdapterVramSet { get; set; }

    private (string caption, bool isNvidia, double totalMb) AdapterInfoForDisplayAndDetection()
    {
        try
        {
            // which phys GPU is the game hammering? use the live counter sample
            _ = PerfCounters.Max3DPercentForPid(_pid, out string luid);
            if (string.IsNullOrEmpty(luid))
                return ("", false, 0);

            var adapter = DxgiAdapters.FindByPerfCounterLuid(luid);
            if (adapter is null)
            {
                // fall back to the busiest NVIDIA adapter via NVML
                if (Nvml.Available)
                    return (ShortAdapterName(Nvml.Name), true, Nvml.Available && Nvml.TryGetSnapshot(out _, out double t, out _, out _, out _, out _) ? t : 0);
                return ("", false, 0);
            }

            bool isNvidia = adapter.Description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
            double totalMb = adapter.DedicatedVideoMemory / (1024.0 * 1024.0);
            return (ShortAdapterName(adapter.Description), isNvidia, totalMb);
        }
        catch
        {
            return ("", false, 0);
        }
    }

    /// <summary>Short display form: "RTX 3050", "Radeon 680M", "Basic Render Driver".</summary>
    public static string ShortAdapterName(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        string s = description
            .Replace("(TM)", "").Replace("(R)", "")
            .Replace("AMD RadeonT", "Radeon")
            .Replace("Graphics", "")
            .Replace("NVIDIA GeForce ", "").Replace("NVIDIA ", "")
            .Trim();

        // prefer the first two tokens ("RTX 3050", "Radeon 680M"); fall back to the full trimmed name
        var words = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string two = words.Length >= 2 ? words[0] + " " + words[1] : (words.Length == 1 ? words[0] : s);
        return two.Length <= 18 ? two : s;
    }

    // ---------- sampling ----------

    private void SampleCpu()
    {
        var p = _targetProcess;
        if (p is null || !GameModeActive)
        {
            _cpuPercent = 0;
            return;
        }
        try
        {
            p.Refresh();
            var nowTotal = p.TotalProcessorTime;
            var now = DateTime.UtcNow;
            double elapsedMs = (now - _lastCpuSample).TotalMilliseconds;
            if (elapsedMs > 50)
            {
                double cpuMs = (nowTotal - _lastCpuTotal).TotalMilliseconds;
                _cpuPercent = Math.Clamp(cpuMs / elapsedMs / Environment.ProcessorCount * 100.0, 0, 100);
                _lastCpuTotal = nowTotal;
                _lastCpuSample = now;
            }
        }
        catch
        {
            _cpuPercent = 0;
        }
    }

    private void SampleGpuAndMemory()
    {
        if (!GameModeActive)
        {
            Gpu3DPercent = 0;
            VramUsedMb = 0;
            GpuTempC = null;
            GpuPowerW = null;
            return;
        }

        PerfCounters.Collect(); // rate counters: collect once per tick, then read both arrays

        try
        {
            var use = PerfCounters.Max3DPercentForPid(_pid, out string luid);
            Gpu3DPercent = use ?? 0;
            if (!string.IsNullOrEmpty(luid))
                TryRefreshAdapterCaption(luid);
        }
        catch
        {
            Gpu3DPercent = 0;
        }

        try
        {
            VramUsedMb = PerfCounters.GpuDedicatedMemoryMbForPid(_pid) ?? 0;
        }
        catch
        {
            VramUsedMb = 0;
        }

        if (IsNvidiaAdapter && Nvml.Available &&
            Nvml.TryGetSnapshot(out _, out _, out _, out _, out uint tempC, out double powerW))
        {
            GpuTempC = tempC;
            GpuPowerW = powerW;
        }
        else
        {
            GpuTempC = null;
            GpuPowerW = null;
        }
    }

    private void TryRefreshAdapterCaption(string luid)
    {
        if (!string.IsNullOrEmpty(AdapterCaption))
            return; // stable for the session
        var adapter = DxgiAdapters.FindByPerfCounterLuid(luid);
        if (adapter is not null)
        {
            IsNvidiaAdapter = adapter.Description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
            AdapterCaption = ShortAdapterName(adapter.Description);
            AdapterVramTotalMb = adapter.DedicatedVideoMemory / (1024.0 * 1024.0);
            IsAdapterVramSet = AdapterVramTotalMb > 0;
        }
    }

    // ---------- FPS via ETW ----------

    private void StartEtw(int pid)
    {
        StopEtw();
        _presents.Clear();
        try
        {
            const ulong DxgKrnlPresent = 0x0000040000000000UL;
            _etw = new TraceEventSession("MinePixGameStrip", TraceEventSessionOptions.Create);
            _etw.Source.Dynamic.All += ev =>
            {
                try
                {
                    if (ev.ProviderGuid == new Guid("{802EC45A-1E99-4B83-9920-87C98277BA9D}"))
                    {
                        if ((int)ev.Task == 14 && (int)ev.Opcode == 1)
                            _presents.Enqueue(DateTime.UtcNow);
                    }
                    else if (ev.ProviderGuid == new Guid("{CA11C036-0102-4A2D-A6AD-F03CFED5D3C9}"))
                    {
                        if ((int)ev.ID == 42)
                            _presents.Enqueue(DateTime.UtcNow);
                    }
                }
                catch { }
            };
            _etw.EnableProvider("Microsoft-Windows-DxgKrnl", TraceEventLevel.Informational, DxgKrnlPresent,
                new TraceEventProviderOptions { ProcessIDFilter = new List<int> { pid } });
            _etw.EnableProvider("Microsoft-Windows-DXGI", TraceEventLevel.Informational, 0,
                new TraceEventProviderOptions { ProcessIDFilter = new List<int> { pid }, EventIDsToEnable = new List<int> { 42 } });
            Task.Run(() =>
            {
                try { _etw?.Source.Process(); } catch { }
            });
        }
        catch (Exception ex)
        {
            _etwBroken = true;
            Fps = null;
            try { StopEtw(); } catch { }
            Log("ETW start failed: " + ex.Message);
        }
    }

    private void StopEtw()
    {
        try
        {
            var etw = _etw;
            _etw = null;
            etw?.Source.StopProcessing();
            etw?.Dispose();
        }
        catch { }
        Fps = null;
    }

    private void PruneFpsWindow()
    {
        if (!GameModeActive)
        {
            Fps = null;
            return;
        }
        if (_etwBroken)
        {
            Fps = null;
            return;
        }
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-1);
        while (_presents.TryPeek(out var ts) && ts < cutoff)
            _presents.TryDequeue(out _);
        Fps = _presents.Count;
    }

    // ---------- helpers ----------

    private void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "game.log"), $"{DateTime.Now:o} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static IReadOnlyList<string> DefaultExclusions => new[]
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe",
        "code.exe", "devenv.exe", "winword.exe", "excel.exe", "powerpnt.exe", "outlook.exe", "onenote.exe",
        "notepad.exe", "msteams.exe", "slack.exe", "zoom.exe", "vlc.exe",
        "mpc-hc.exe", "mpc-hc64.exe", "mpv.exe", "spotify.exe",
        "discord.exe", "telegram.exe", "signal.exe",
        "explorer.exe", "shelltaskbar.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe",
        "searchhost.exe", "textinputhost.exe", "applicationframehost.exe", "msedgewebview2.exe",
        "windowsterminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "soffice.exe", "searchapplication.exe", "dwm.exe", "minepix.exe",
    };

    // ---------- Win32 ----------

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WinRect lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint CbSize;
        public WinRect Monitor;
        public WinRect Work;
        public uint Flags;
    }
}
