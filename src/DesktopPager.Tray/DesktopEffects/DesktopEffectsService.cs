using System;
using System.IO;
using System.Text;
using System.Windows.Threading;

namespace DesktopPager.Tray.DesktopEffects;

/// <summary>
/// Coordina gli "effetti desktop 3D": cubo di paginazione (Ctrl + tasto destro
/// trascinando sul desktop) ed effetto gelatina delle finestre allo
/// spostamento. Possiede gli hook globali e gli overlay, e ricorda lo stato
/// abilitato tra un avvio e l'altro.
/// </summary>
public sealed class DesktopEffectsService : IDisposable
{
    private const double YawPerPixel = 0.3; // ~300px di trascinamento = 90°

    private readonly Action<int> _changePage;   // dir<0 precedente, dir>0 successiva
    private readonly uint _selfPid = (uint)Environment.ProcessId;

    private readonly MoveSizeHook _moveHook = new();
    private readonly GlobalMouseHook _mouseHook = new();
    private readonly DispatcherTimer _wobblePoll = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private readonly TaskbarTweaks _taskbar = new();
    // Explorer rifa' l'impaginazione della barra quando apri/chiudi finestre e
    // rimette le freccette: un giro al secondo basta a riprendersele senza
    // pesare (e' solo una lettura di stile finche' non c'e' nulla da fare)
    private readonly DispatcherTimer _taskbarPoll = new() { Interval = TimeSpan.FromSeconds(1) };

    private WobbleOverlay? _wobble;
    private CubeOverlay? _cube;

    private IntPtr _movingHwnd;
    private bool _cubeDragging;
    private int _cubeStartX;

    private bool _wobbleEnabled;
    private bool _cubeEnabled;

    public DesktopEffectsService(Action<int> changePage)
    {
        _changePage = changePage ?? throw new ArgumentNullException(nameof(changePage));

        _moveHook.MoveStart += OnMoveStart;
        _moveHook.MoveEnd += OnMoveEnd;
        _wobblePoll.Tick += (_, _) => PollMovingWindow();
        _taskbarPoll.Tick += (_, _) => _taskbar.Sync();
        _mouseHook.OnMouse = OnMouse;
        BarStyle.Changed += OnBarStyleChanged;

        Load();
    }

    private void OnBarStyleChanged() => _taskbar.RefreshTint();

    public bool WobbleEnabled
    {
        get => _wobbleEnabled;
        set
        {
            if (_wobbleEnabled == value)
            {
                return;
            }
            _wobbleEnabled = value;
            if (value)
            {
                _moveHook.Install();
            }
            else
            {
                _moveHook.Uninstall();
                _wobblePoll.Stop();
                _wobble?.Settle();
            }
            Save();
        }
    }

    public bool CubeEnabled
    {
        get => _cubeEnabled;
        set
        {
            if (_cubeEnabled == value)
            {
                return;
            }
            _cubeEnabled = value;
            if (!value && _cubeDragging)
            {
                _cubeDragging = false;
                _cube?.Cancel();
            }
            SyncMouseHook();
            Save();
        }
    }

    // --- barra delle applicazioni di Windows -------------------------------

    /// <summary>Come si sfoglia l'elenco applicazioni della barra di Windows.</summary>
    public TaskbarScrollMode TaskbarScroll
    {
        get => _taskbar.ScrollMode;
        set
        {
            if (_taskbar.ScrollMode == value)
            {
                return;
            }
            _taskbar.ScrollMode = value;
            SyncMouseHook();
            SyncTaskbarPoll();
            Save();
        }
    }

    /// <summary>Colora la barra di Windows con il colore della nostra barra.</summary>
    public bool TaskbarTint
    {
        get => _taskbar.TintEnabled;
        set
        {
            if (_taskbar.TintEnabled == value)
            {
                return;
            }
            _taskbar.TintEnabled = value;
            SyncTaskbarPoll();
            Save();
        }
    }

    /// <summary>
    /// Il timer serve a entrambi: alle freccette, che Explorer rimette quando
    /// rifa' l'impaginazione, e alla tinta, che sparisce se Explorer riparte.
    /// </summary>
    private void SyncTaskbarPoll()
    {
        if (_taskbar.ScrollMode != TaskbarScrollMode.Off || _taskbar.TintEnabled)
        {
            _taskbarPoll.Start();
        }
        else
        {
            _taskbarPoll.Stop();
        }
    }

    /// <summary>
    /// L'hook del mouse serve sia al cubo sia alla rotellina sulla barra:
    /// tienilo installato finche' almeno uno dei due lo vuole.
    /// </summary>
    private void SyncMouseHook()
    {
        if (_cubeEnabled || _taskbar.ScrollMode != TaskbarScrollMode.Off)
        {
            _mouseHook.Install();
        }
        else
        {
            _mouseHook.Uninstall();
        }
    }

    // --- gelatina (wobble) -------------------------------------------------

    private void OnMoveStart(IntPtr hwnd)
    {
        if (!_wobbleEnabled || IsOwnOrShell(hwnd))
        {
            return;
        }

        var snap = Capture.Window(hwnd, out var rect);
        if (snap is null)
        {
            return;
        }

        _wobble ??= new WobbleOverlay();
        _movingHwnd = hwnd;
        _wobble.Begin(snap, rect);
        _wobblePoll.Start();
    }

    private void OnMoveEnd(IntPtr hwnd)
    {
        _wobblePoll.Stop();
        _movingHwnd = IntPtr.Zero;
        _wobble?.Settle();
    }

    private void PollMovingWindow()
    {
        if (_movingHwnd == IntPtr.Zero || _wobble is null)
        {
            return;
        }
        if (EffectsNative.GetWindowRect(_movingHwnd, out var r))
        {
            _wobble.SetTarget(r.Left, r.Top);
        }
    }

    // --- cubo di paginazione ----------------------------------------------

    private bool OnMouse(int msg, int x, int y, uint mouseData)
    {
        switch (msg)
        {
            case EffectsNative.WM_MOUSEWHEEL:
            case EffectsNative.WM_MOUSEHWHEEL:
                return _taskbar.HandleWheel(msg, x, y, mouseData);

            case EffectsNative.WM_RBUTTONDOWN:
                if (_cubeEnabled && EffectsNative.CtrlDown && PointOnDesktop(x, y))
                {
                    var screen = Capture.Screen(
                        (int)System.Windows.SystemParameters.VirtualScreenLeft,
                        (int)System.Windows.SystemParameters.VirtualScreenTop,
                        Math.Max(1, (int)System.Windows.SystemParameters.PrimaryScreenWidth),
                        Math.Max(1, (int)System.Windows.SystemParameters.PrimaryScreenHeight));
                    _cube ??= CreateCube();
                    _cube.Begin(screen);
                    _cubeDragging = true;
                    _cubeStartX = x;
                    return true; // consuma: niente menu contestuale del desktop
                }
                return false;

            case EffectsNative.WM_MOUSEMOVE:
                if (_cubeDragging && _cube is not null)
                {
                    _cube.SetYaw((x - _cubeStartX) * YawPerPixel);
                    return true;
                }
                return false;

            case EffectsNative.WM_RBUTTONUP:
                if (_cubeDragging)
                {
                    _cubeDragging = false;
                    _cube?.Finish();
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    private CubeOverlay CreateCube()
    {
        var cube = new CubeOverlay();
        cube.Committed += dir => { if (dir != 0) _changePage(dir); };
        return cube;
    }

    private static bool PointOnDesktop(int x, int y)
    {
        var hwnd = EffectsNative.WindowFromPoint(new EffectsNative.POINT { x = x, y = y });
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        for (var h = hwnd; h != IntPtr.Zero; h = EffectsNative.GetParent(h))
        {
            var cls = ClassOf(h);
            if (cls is "SysListView32" or "SHELLDLL_DefView" or "WorkerW" or "Progman")
            {
                return true;
            }
        }
        return false;
    }

    private bool IsOwnOrShell(IntPtr hwnd)
    {
        EffectsNative.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _selfPid)
        {
            return true;
        }
        var cls = ClassOf(hwnd);
        return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        EffectsNative.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // --- persistenza -------------------------------------------------------

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopPager3D-OS", "effects.cfg");

    private void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return;
            }
            foreach (var line in File.ReadAllLines(ConfigPath))
            {
                var i = line.IndexOf('=');
                if (i <= 0)
                {
                    continue;
                }
                var key = line[..i].Trim();
                var value = line[(i + 1)..].Trim();
                var on = value == "1";
                switch (key)
                {
                    case "wobble":
                        WobbleEnabled = on;
                        break;
                    case "cube":
                        CubeEnabled = on;
                        break;
                    case "tbscroll":
                        TaskbarScroll = value switch
                        {
                            "1" => TaskbarScrollMode.Wheel,
                            "2" => TaskbarScrollMode.TiltWheel,
                            _ => TaskbarScrollMode.Off
                        };
                        break;
                    case "tbtint":
                        TaskbarTint = on;
                        break;
                }
            }
        }
        catch
        {
            // configurazione illeggibile: si parte con gli effetti spenti
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath,
                $"wobble={(_wobbleEnabled ? 1 : 0)}\n" +
                $"cube={(_cubeEnabled ? 1 : 0)}\n" +
                $"tbscroll={(int)_taskbar.ScrollMode}\n" +
                $"tbtint={(_taskbar.TintEnabled ? 1 : 0)}\n");
        }
        catch
        {
            // impossibile salvare: non è critico
        }
    }

    public void Dispose()
    {
        BarStyle.Changed -= OnBarStyleChanged;
        _wobblePoll.Stop();
        _taskbarPoll.Stop();
        _moveHook.Dispose();
        _mouseHook.Dispose();
        // prima di tutto rimetti a posto la barra di Windows: e' roba di
        // Explorer, non deve restare ritoccata dopo che siamo usciti
        _taskbar.Dispose();
        _wobble?.Close();
        _cube?.Close();
    }
}
