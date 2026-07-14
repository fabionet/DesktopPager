using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;

namespace DesktopPager.Tray.DesktopEffects;

/// <summary>
/// Overlay a schermo intero con un cubo 3D le cui facce sono lo screenshot del
/// desktop. Trascinando (Ctrl+tasto destro) il cubo ruota attorno all'asse Y;
/// al rilascio scatta alla faccia più vicina e, se ha girato abbastanza, chiede
/// il cambio pagina del desktop, poi svanisce rivelando la nuova pagina reale.
/// </summary>
internal sealed class CubeOverlay : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    /// <summary>Chiamato allo scatto: -1 pagina precedente, +1 successiva, 0 nessuna.</summary>
    public event Action<int>? Committed;

    private readonly Viewport3D _viewport = new();
    private readonly AxisAngleRotation3D _rot = new() { Axis = new Vector3D(0, 1, 0), Angle = 0 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private enum Mode { Idle, Dragging, Snapping, Fading }
    private Mode _mode = Mode.Idle;
    private double _yaw;
    private double _yawTarget;
    private int _dir;
    private bool _committed;

    public bool Active => _mode != Mode.Idle;

    public CubeOverlay()
    {
        // niente AllowsTransparency: in host WinForms senza Application WPF
        // bloccherebbe Show(). Sfondo nero pieno; bounds espliciti (Maximized
        // non è ammesso con ShowActivated=false).
        WindowStyle = WindowStyle.None;
        Background = WpfBrushes.Black;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        Content = _viewport;

        _timer.Tick += (_, _) => Step();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle,
            ex | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
    }

    public void Begin(BitmapSource screen)
    {
        var sw = SystemParameters.PrimaryScreenWidth;
        var sh = SystemParameters.PrimaryScreenHeight;
        Left = 0;
        Top = 0;
        Width = sw;
        Height = sh;
        var aspect = sw > 0 && sh > 0 ? sw / sh : 16.0 / 9.0;
        BuildScene(screen, aspect);

        _yaw = 0;
        _committed = false;
        _mode = Mode.Dragging;
        Opacity = 1;
        Apply();
        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>Imposta la rotazione dal trascinamento (gradi, limitata a ±90).</summary>
    public void SetYaw(double deg)
    {
        if (_mode != Mode.Dragging)
        {
            return;
        }
        _yaw = Clamp(deg, -90, 90);
        Apply();
    }

    /// <summary>Fine trascinamento: scatta alla faccia più vicina.</summary>
    public void Finish()
    {
        if (_mode != Mode.Dragging)
        {
            return;
        }

        if (_yaw > 45)
        {
            _dir = 1; _yawTarget = 90;
        }
        else if (_yaw < -45)
        {
            _dir = -1; _yawTarget = -90;
        }
        else
        {
            _dir = 0; _yawTarget = 0;
        }

        _mode = Mode.Snapping;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    /// <summary>Annulla immediatamente (es. effetto disattivato).</summary>
    public void Cancel()
    {
        _timer.Stop();
        _mode = Mode.Idle;
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Step()
    {
        switch (_mode)
        {
            case Mode.Snapping:
                _yaw += (_yawTarget - _yaw) * 0.22;
                Apply();
                if (Math.Abs(_yawTarget - _yaw) < 0.5)
                {
                    _yaw = _yawTarget;
                    Apply();
                    if (!_committed)
                    {
                        _committed = true;
                        try { Committed?.Invoke(_dir); } catch { /* il paging non deve rompere l'effetto */ }
                    }
                    _mode = Mode.Fading;
                }
                break;

            case Mode.Fading:
                Opacity -= 0.14;
                if (Opacity <= 0.02)
                {
                    Opacity = 0;
                    Hide();
                    _mode = Mode.Idle;
                    _timer.Stop();
                }
                break;
        }
    }

    private void Apply() => _rot.Angle = _yaw;

    private void BuildScene(BitmapSource screen, double aspect)
    {
        var ax = aspect;   // mezza larghezza (X)
        var ay = 1.0;      // mezza altezza (Y)
        var az = aspect;   // mezza profondità (Z) — così le facce laterali sono piene schermo

        var material = new DiffuseMaterial(new ImageBrush(screen)
        {
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.Absolute
        });

        var group = new Model3DGroup();
        // luci: la faccia frontale piena, le laterali digradano naturalmente
        group.Children.Add(new AmbientLight(WpfColor.FromRgb(90, 90, 90)));
        group.Children.Add(new DirectionalLight(WpfColors.White, new Vector3D(0, 0, -1)));

        // fronte (+Z), retro (-Z), sinistra (-X), destra (+X), sopra (+Y), sotto (-Y)
        AddFace(group, material,
            new Point3D(-ax, ay, az), new Point3D(ax, ay, az),
            new Point3D(ax, -ay, az), new Point3D(-ax, -ay, az));
        AddFace(group, material,
            new Point3D(ax, ay, -az), new Point3D(-ax, ay, -az),
            new Point3D(-ax, -ay, -az), new Point3D(ax, -ay, -az));
        AddFace(group, material,
            new Point3D(-ax, ay, -az), new Point3D(-ax, ay, az),
            new Point3D(-ax, -ay, az), new Point3D(-ax, -ay, -az));
        AddFace(group, material,
            new Point3D(ax, ay, az), new Point3D(ax, ay, -az),
            new Point3D(ax, -ay, -az), new Point3D(ax, -ay, az));
        AddFace(group, material,
            new Point3D(-ax, ay, -az), new Point3D(ax, ay, -az),
            new Point3D(ax, ay, az), new Point3D(-ax, ay, az));
        AddFace(group, material,
            new Point3D(-ax, -ay, az), new Point3D(ax, -ay, az),
            new Point3D(ax, -ay, -az), new Point3D(-ax, -ay, -az));

        var visual = new ModelVisual3D
        {
            Content = group,
            Transform = new RotateTransform3D(_rot)
        };

        _viewport.Children.Clear();
        _viewport.Children.Add(visual);

        var camZ = az + 2.7; // distanza per riempire (con piccolo margine) la faccia frontale
        _viewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0, camZ),
            LookDirection = new Vector3D(0, 0, -1),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 45
        };
    }

    private static void AddFace(Model3DGroup group, Material material,
        Point3D p0, Point3D p1, Point3D p2, Point3D p3)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection { p0, p1, p2, p3 },
            TextureCoordinates = new PointCollection
            {
                new WpfPoint(0, 0), new WpfPoint(1, 0), new WpfPoint(1, 1), new WpfPoint(0, 1)
            },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };

        group.Children.Add(new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material // visibile da entrambi i lati: niente facce nere
        });
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
