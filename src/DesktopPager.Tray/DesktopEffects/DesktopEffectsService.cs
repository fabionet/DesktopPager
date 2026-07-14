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
        _mouseHook.OnMouse = OnMouse;

        Load();
    }

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
            if (value)
            {
                _mouseHook.Install();
            }
            else
            {
                _mouseHook.Uninstall();
                if (_cubeDragging)
                {
                    _cubeDragging = false;
                    _cube?.Cancel();
                }
            }
            Save();
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

    private bool OnMouse(int msg, int x, int y)
    {
        switch (msg)
        {
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
                var on = line[(i + 1)..].Trim() == "1";
                if (key == "wobble")
                {
                    WobbleEnabled = on;
                }
                else if (key == "cube")
                {
                    CubeEnabled = on;
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
                $"wobble={(_wobbleEnabled ? 1 : 0)}\ncube={(_cubeEnabled ? 1 : 0)}\n");
        }
        catch
        {
            // impossibile salvare: non è critico
        }
    }

    public void Dispose()
    {
        _wobblePoll.Stop();
        _moveHook.Dispose();
        _mouseHook.Dispose();
        _wobble?.Close();
        _cube?.Close();
    }
}
