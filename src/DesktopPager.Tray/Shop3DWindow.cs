using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfPath = System.Windows.Shapes.Path;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace DesktopPager.Tray;

/// <summary>
/// Vista 3D Game: esplorazione in prima persona dei dischi e delle cartelle
/// come dentro un negozio. All'ingresso ci sono i "banchi" per dischi e
/// cartelle speciali; entrando in uno si cammina in una stanza con i suoi
/// file e sottocartelle disposti in 3D. Movimento con frecce/WASD, Invio per
/// entrare/aprire, Backspace per tornare indietro, Esc per uscire.
/// </summary>
public sealed class Shop3DWindow : Window
{
    private sealed record Entry(string Name, string FullPath, bool IsContainer);

    private const int MaxEntries = 48;
    private const int Columns = 5;
    private const double SpacingX = 4.4;
    private const double SpacingZ = 4.8;
    private const double MoveSpeed = 0.13;
    private const double TurnSpeed = 0.035; // radianti/tick

    // rimpicciolimento dei banchi all'avvicinarsi (così restano leggibili per intero)
    private const double ShrinkNear = 3.0;   // sotto questa distanza scala minima
    private const double ShrinkFar = 8.0;     // sopra questa distanza scala piena
    private const double ShrinkMin = 0.5;

    private readonly PerspectiveCamera _camera = new() { FieldOfView = 55 };
    private readonly Model3DGroup _root = new();
    private readonly Viewport3D _viewport = new();
    private readonly DispatcherTimer _loop = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly HashSet<Key> _keys = new();

    private readonly TextBlock _pathLabel;
    private readonly TextBlock _focusLabel;

    private readonly List<Entry> _entries = new();
    private readonly List<Point3D> _boothPos = new();
    private readonly List<Model3DGroup> _boothGroups = new();
    private readonly Dictionary<GeometryModel3D, int> _modelToEntry = new();
    private GeometryModel3D? _focusRing;

    private string? _current;    // null = "Questo PC"
    private int _focus = -1;
    private double _px, _pz, _yaw;
    private double _roomHalfW, _roomBackZ;

    // ingresso cinematografico: schermo nero, logo Windows a 4 colori che si
    // apre come una porta, con barra di avanzamento del caricamento
    private readonly Grid _rootGrid = new();
    private readonly Grid _loadingOverlay = new();
    private readonly Canvas _logoCanvas = new();
    private readonly WpfRectangle[] _logoPanes = new WpfRectangle[4];
    private readonly WpfRectangle _progressFill = new();
    private const double ProgressWidth = 340;
    private bool _introDone;

    public Shop3DWindow()
    {
        Title = "DesktopPager3D-OS - Vista 3D Game";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        // sfondo a gradiente (cielo) per staccare bene gli oggetti
        Background = new LinearGradientBrush(
            Color.FromRgb(36, 74, 118),
            Color.FromRgb(9, 13, 24),
            new Point(0.5, 0), new Point(0.5, 1));
        ShowInTaskbar = false;
        Topmost = true;

        _camera.UpDirection = new Vector3D(0, 1, 0);
        _viewport.Camera = _camera;
        _viewport.Children.Add(new ModelVisual3D { Content = _root });

        _pathLabel = Hud(20, FontWeights.SemiBold, HorizontalAlignment.Left, VerticalAlignment.Top,
            new Thickness(24, 18, 0, 0));
        _focusLabel = Hud(17, FontWeights.Normal, HorizontalAlignment.Center, VerticalAlignment.Bottom,
            new Thickness(0, 0, 0, 46));
        var hint = Hud(12, FontWeights.Normal, HorizontalAlignment.Center, VerticalAlignment.Bottom,
            new Thickness(0, 0, 0, 22));
        hint.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 165));
        hint.Text = "frecce / WASD = muoviti e gira   •   Invio = entra o apri   •   Backspace = indietro   •   Esc = esci";

        _rootGrid.Children.Add(_viewport);
        _rootGrid.Children.Add(_pathLabel);
        _rootGrid.Children.Add(_focusLabel);
        _rootGrid.Children.Add(hint);
        BuildIntroOverlays();
        _rootGrid.Children.Add(_loadingOverlay);
        Content = _rootGrid;

        _loop.Tick += (_, _) => Tick();
        Loaded += (_, _) => { Activate(); Focus(); Navigate(null); PlayIntro(); };
        KeyDown += OnKeyDown;
        KeyUp += (_, e) => _keys.Remove(e.Key);
        MouseLeftButtonUp += OnClick;
        Closed += (_, _) => _loop.Stop();
    }

    private static TextBlock Hud(double size, FontWeight weight, HorizontalAlignment h, VerticalAlignment v, Thickness margin)
    {
        return new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size,
            FontWeight = weight,
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Margin = margin,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9
            }
        };
    }

    // --- ingresso cinematografico -----------------------------------------

    private const double LogoPane = 84;   // lato di un pannello del logo
    private const double LogoGap = 10;     // fuga tra i pannelli

    private void BuildIntroOverlays()
    {
        // overlay a schermo nero
        _loadingOverlay.Background = System.Windows.Media.Brushes.Black;
        _loadingOverlay.Visibility = Visibility.Collapsed;

        // logo Windows a 4 colori (stile Windows 7), centrato, che si apre come una porta
        var logoSize = LogoPane * 2 + LogoGap;
        _logoCanvas.Width = logoSize;
        _logoCanvas.Height = logoSize;
        _logoCanvas.HorizontalAlignment = HorizontalAlignment.Center;
        _logoCanvas.VerticalAlignment = VerticalAlignment.Center;
        _logoCanvas.Margin = new Thickness(0, 0, 0, 90);

        Color[] cols =
        {
            Color.FromRgb(0xE6, 0x3B, 0x2E), // rosso  (alto-sx)
            Color.FromRgb(0x6F, 0xBF, 0x2E), // verde  (alto-dx)
            Color.FromRgb(0x00, 0x9D, 0xE0), // blu    (basso-sx)
            Color.FromRgb(0xF7, 0xB6, 0x00)  // giallo (basso-dx)
        };
        (double x, double y)[] pos =
        {
            (0, 0), (LogoPane + LogoGap, 0),
            (0, LogoPane + LogoGap), (LogoPane + LogoGap, LogoPane + LogoGap)
        };
        for (var i = 0; i < 4; i++)
        {
            var pane = new WpfRectangle
            {
                Width = LogoPane,
                Height = LogoPane,
                RadiusX = 8,
                RadiusY = 8,
                Fill = new SolidColorBrush(cols[i]),
                RenderTransform = new TranslateTransform(),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            _logoPanes[i] = pane;
            Canvas.SetLeft(pane, pos[i].x);
            Canvas.SetTop(pane, pos[i].y);
            _logoCanvas.Children.Add(pane);
        }

        // barra di avanzamento sotto il logo
        var track = new System.Windows.Controls.Border
        {
            Width = ProgressWidth,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 52)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 150, 0, 0)
        };
        _progressFill.Width = 0;
        _progressFill.Height = 8;
        _progressFill.RadiusX = 4;
        _progressFill.RadiusY = 4;
        _progressFill.HorizontalAlignment = HorizontalAlignment.Left;
        _progressFill.Fill = new SolidColorBrush(Color.FromRgb(0x35, 0x9D, 0xE0));
        track.Child = _progressFill;

        var loadText = new TextBlock
        {
            Text = "Caricamento di DesktopPager3D-OS…",
            Foreground = System.Windows.Media.Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 205, 0, 0)
        };

        _loadingOverlay.Children.Add(_logoCanvas);
        _loadingOverlay.Children.Add(track);
        _loadingOverlay.Children.Add(loadText);
    }

    private void PlayIntro()
    {
        _introDone = false;

        // camera in vista aerea sopra i banchi, guardando in basso (dietro il nero)
        _camera.Position = new Point3D(0, 15, -1);
        _camera.LookDirection = new Vector3D(0, -1, -0.18);

        // reset logo e barra
        foreach (var pane in _logoPanes)
        {
            pane.Opacity = 1;
            var t = (TranslateTransform)pane.RenderTransform;
            t.X = 0;
            t.Y = 0;
        }
        _progressFill.Width = 0;
        _loadingOverlay.Opacity = 1;
        _loadingOverlay.Visibility = Visibility.Visible;

        // fase 1: caricamento con barra di avanzamento (~2.6s)
        _progressFill.BeginAnimation(WidthProperty,
            new DoubleAnimation(0, ProgressWidth, TimeSpan.FromMilliseconds(2600))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });

        After(2800, () =>
        {
            // fase 2: il logo si apre come una porta (4 pannelli verso gli angoli),
            // più lentamente, mentre il nero svanisce rivelando la vista dall'alto
            OpenLogoDoor();
            _loadingOverlay.BeginAnimation(OpacityProperty, Fade(1, 0, 1400));
            After(1200, LandCamera);
            After(3600, () =>
            {
                _loadingOverlay.Visibility = Visibility.Collapsed;
                FinishIntro();
            });
        });
    }

    private void OpenLogoDoor()
    {
        var dur = TimeSpan.FromMilliseconds(1600);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var spread = Math.Max(ActualWidth, ActualHeight);
        (double dx, double dy)[] dir =
        {
            (-spread, -spread), (spread, -spread), (-spread, spread), (spread, spread)
        };
        for (var i = 0; i < 4; i++)
        {
            var t = (TranslateTransform)_logoPanes[i].RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dir[i].dx, dur) { EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dir[i].dy, dur) { EasingFunction = ease });
        }
    }

    private void LandCamera()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var posAnim = new Point3DAnimation(new Point3D(0, 15, -1), new Point3D(0, 1.65, 10),
            TimeSpan.FromMilliseconds(2000)) { EasingFunction = ease };
        var lookAnim = new Vector3DAnimation(new Vector3D(0, -1, -0.18), new Vector3D(0, -0.08, -1),
            TimeSpan.FromMilliseconds(2000)) { EasingFunction = ease };
        _camera.BeginAnimation(PerspectiveCamera.PositionProperty, posAnim);
        _camera.BeginAnimation(PerspectiveCamera.LookDirectionProperty, lookAnim);
    }

    private void FinishIntro()
    {
        if (_introDone)
        {
            return;
        }

        // libera la camera dalle animazioni e passa al controllo manuale
        _camera.BeginAnimation(PerspectiveCamera.PositionProperty, null);
        _camera.BeginAnimation(PerspectiveCamera.LookDirectionProperty, null);
        _loadingOverlay.Visibility = Visibility.Collapsed;
        _px = 0;
        _pz = 10;
        _yaw = 0;
        _introDone = true;
        if (!_loop.IsEnabled)
        {
            _loop.Start();
        }
    }

    private static DoubleAnimation Fade(double from, double to, int ms) =>
        new(from, to, TimeSpan.FromMilliseconds(ms));

    private void After(int ms, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); action(); };
        t.Start();
    }

    // --- input ------------------------------------------------------------

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!_introDone) { FinishIntro(); return; } // salta l'intro
            Close();
            return;
        }

        if (!_introDone)
        {
            return; // ignora gli altri comandi durante l'ingresso
        }

        switch (e.Key)
        {
            case Key.Enter: ActivateFocus(); return;
            case Key.Back: GoUp(); return;
            default: _keys.Add(e.Key); break;
        }
    }

    private void Tick()
    {
        if (!_introDone)
        {
            return;
        }

        var fwd = new Vector3D(Math.Sin(_yaw), 0, -Math.Cos(_yaw));
        var right = new Vector3D(Math.Cos(_yaw), 0, Math.Sin(_yaw));

        if (_keys.Contains(Key.Left)) _yaw -= TurnSpeed;
        if (_keys.Contains(Key.Right)) _yaw += TurnSpeed;
        if (_keys.Contains(Key.Up) || _keys.Contains(Key.W)) { _px += fwd.X * MoveSpeed; _pz += fwd.Z * MoveSpeed; }
        if (_keys.Contains(Key.Down) || _keys.Contains(Key.S)) { _px -= fwd.X * MoveSpeed; _pz -= fwd.Z * MoveSpeed; }
        if (_keys.Contains(Key.A)) { _px -= right.X * MoveSpeed; _pz -= right.Z * MoveSpeed; }
        if (_keys.Contains(Key.D)) { _px += right.X * MoveSpeed; _pz += right.Z * MoveSpeed; }

        // resta dentro la stanza
        _px = Math.Clamp(_px, -_roomHalfW + 1, _roomHalfW - 1);
        _pz = Math.Clamp(_pz, _roomBackZ + 1.2, 11);

        _camera.Position = new Point3D(_px, 1.65, _pz);
        _camera.LookDirection = new Vector3D(fwd.X, -0.08, fwd.Z);

        UpdateBoothScale();
        UpdateFocus();
    }

    // I banchi vicini si rimpiccioliscono per restare leggibili per intero.
    private void UpdateBoothScale()
    {
        for (var i = 0; i < _boothGroups.Count && i < _boothPos.Count; i++)
        {
            var cx = _boothPos[i].X;
            var cz = _boothPos[i].Z;
            var dx = cx - _px;
            var dz = cz - _pz;
            var dist = Math.Sqrt(dx * dx + dz * dz);

            double s;
            if (dist >= ShrinkFar) s = 1.0;
            else if (dist <= ShrinkNear) s = ShrinkMin;
            else s = ShrinkMin + (1.0 - ShrinkMin) * (dist - ShrinkNear) / (ShrinkFar - ShrinkNear);

            // scala attorno alla base del banco (mantiene i piedi a terra)
            _boothGroups[i].Transform = new ScaleTransform3D(s, s, s, cx, 0, cz);
        }
    }

    private void UpdateFocus()
    {
        // banco piu' vicino al punto ~3.5 unita' davanti alla vista
        var fwd = new Vector3D(Math.Sin(_yaw), 0, -Math.Cos(_yaw));
        var probe = new Point3D(_px + fwd.X * 3.5, 0, _pz + fwd.Z * 3.5);

        var best = -1;
        var bestDist = 3.0;
        for (var i = 0; i < _boothPos.Count; i++)
        {
            var dx = _boothPos[i].X - probe.X;
            var dz = _boothPos[i].Z - probe.Z;
            var d = Math.Sqrt(dx * dx + dz * dz);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        if (best != _focus)
        {
            _focus = best;
            _focusLabel.Text = best >= 0
                ? (_entries[best].IsContainer ? "📂 " : "📄 ") + _entries[best].Name
                : "";
        }

        if (_focusRing is not null)
        {
            _focusRing.Transform = best >= 0
                ? new TranslateTransform3D(_boothPos[best].X, 0.03, _boothPos[best].Z)
                : new TranslateTransform3D(0, -100, 0);
        }
    }

    private void OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_introDone)
        {
            return;
        }

        var pos = e.GetPosition(_viewport);
        var hit = VisualTreeHelper.HitTest(_viewport, pos) as RayMeshGeometry3DHitTestResult;
        if (hit?.ModelHit is GeometryModel3D model && _modelToEntry.TryGetValue(model, out var index))
        {
            ActivateEntry(index);
        }
    }

    private void ActivateFocus()
    {
        ActivateEntry(_focus);
    }

    private void ActivateEntry(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        var e = _entries[index];
        if (e.IsContainer)
        {
            Navigate(e.FullPath);
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = e.FullPath, UseShellExecute = true });
            }
            catch
            {
                // apertura fallita
            }
        }
    }

    private void GoUp()
    {
        if (_current is null)
        {
            return; // gia' all'ingresso
        }

        var parent = Directory.GetParent(_current.TrimEnd('\\'));
        Navigate(parent?.FullName);
    }

    // --- navigazione / costruzione stanza ---------------------------------

    private void Navigate(string? path)
    {
        _current = path;
        _entries.Clear();
        _entries.AddRange(ReadEntries(path));
        _pathLabel.Text = path is null ? "🏠 Questo PC" : path;
        BuildRoom();

        // parti davanti all'ingresso guardando dentro
        _yaw = 0;
        _px = 0;
        _pz = 10;
        _focus = -1;
        _focusLabel.Text = "";
    }

    private static IEnumerable<Entry> ReadEntries(string? path)
    {
        var list = new List<Entry>();
        try
        {
            if (path is null)
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady)
                    {
                        continue;
                    }

                    var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disco" : d.VolumeLabel;
                    list.Add(new Entry($"{label} ({d.Name.TrimEnd('\\')})", d.RootDirectory.FullName, true));
                }

                foreach (var sf in new[]
                {
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.MyVideos
                })
                {
                    var p = Environment.GetFolderPath(sf);
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                    {
                        list.Add(new Entry(Path.GetFileName(p.TrimEnd('\\')), p, true));
                    }
                }

                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloads))
                {
                    list.Add(new Entry("Downloads", downloads, true));
                }
            }
            else
            {
                foreach (var dir in Directory.EnumerateDirectories(path).Take(MaxEntries))
                {
                    list.Add(new Entry(Path.GetFileName(dir), dir, true));
                }

                foreach (var file in Directory.EnumerateFiles(path).Take(MaxEntries - list.Count))
                {
                    list.Add(new Entry(Path.GetFileName(file), file, false));
                }
            }
        }
        catch
        {
            // percorso non accessibile: stanza vuota
        }

        return list.Take(MaxEntries);
    }

    private void BuildRoom()
    {
        _root.Children.Clear();
        _boothPos.Clear();
        _boothGroups.Clear();
        _modelToEntry.Clear();

        // luci: ambiente tenue + direzionale per dare volume
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(96, 96, 104)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(210, 210, 220), new Vector3D(-0.4, -1, -0.5)));
        _root.Children.Add(lights);

        var rows = Math.Max(1, (int)Math.Ceiling(_entries.Count / (double)Columns));
        _roomHalfW = Columns * SpacingX / 2 + 2.5;
        _roomBackZ = -(rows * SpacingZ) - 2.5;
        const double frontZ = 12.5;
        const double wallH = 5.0;

        // pavimento a griglia
        var floorBrush = new DiffuseMaterial(TiledBrush(GridTile(), 14));
        _root.Children.Add(Box(_roomHalfW * 2, 0.05, frontZ - _roomBackZ, 0, -0.025,
            (frontZ + _roomBackZ) / 2, floorBrush));

        // pareti
        var wallMat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(28, 30, 40)));
        _root.Children.Add(Box(_roomHalfW * 2, wallH, 0.2, 0, wallH / 2, _roomBackZ, wallMat));           // fondo
        _root.Children.Add(Box(0.2, wallH, frontZ - _roomBackZ, -_roomHalfW, wallH / 2, (frontZ + _roomBackZ) / 2, wallMat)); // sinistra
        _root.Children.Add(Box(0.2, wallH, frontZ - _roomBackZ, _roomHalfW, wallH / 2, (frontZ + _roomBackZ) / 2, wallMat));  // destra

        // insegna con la posizione corrente, in alto sulla parete di fondo
        var sign = _current is null ? "Questo PC" : Path.GetFileName(_current.TrimEnd('\\'));
        if (string.IsNullOrEmpty(sign)) sign = _current ?? "";
        var signBrush = TextBrush(sign, 40, Drawing.Color.FromArgb(230, 30, 120, 200), Drawing.Color.White, out var signAspect);
        var signH = 1.1;
        _root.Children.Add(Quad(signH * signAspect, signH, 0, wallH - 0.9, _roomBackZ + 0.2,
            new EmissiveMaterial(signBrush), facePlusZ: true));

        // banchi
        var accent = ThemeService.Accent;
        var pedestalMat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(44, 46, 58)));
        for (var i = 0; i < _entries.Count; i++)
        {
            var col = i % Columns;
            var row = i / Columns;
            var x = (col - (Columns - 1) / 2.0) * SpacingX;
            var z = -2.0 - row * SpacingZ;
            _boothPos.Add(new Point3D(x, 0, z));

            // ogni banco è un gruppo (così può essere scalato all'avvicinarsi)
            var booth = new Model3DGroup();

            // piedistallo
            var pedestal = Box(1.5, 0.9, 0.7, x, 0.45, z, pedestalMat);
            _modelToEntry[pedestal] = i;
            booth.Children.Add(pedestal);

            // pannello con anteprima/icona
            var img = LoadBrush(_entries[i]);
            if (img is not null)
            {
                var w = 1.5 * Math.Clamp(img.Value.aspect, 0.5, 1.8);
                var panel = Quad(w, 1.5, x, 1.85, z + 0.36, new EmissiveMaterial(img.Value.brush), facePlusZ: true);
                _modelToEntry[panel] = i;
                booth.Children.Add(panel);
            }

            // etichetta col nome
            var label = TextBrush(_entries[i].Name, 26,
                Drawing.Color.FromArgb(220, 16, 18, 28), Drawing.Color.White, out var aspect);
            var labelModel = Quad(1.7, 1.7 / aspect, x, 2.95, z + 0.36, new EmissiveMaterial(label), facePlusZ: true);
            _modelToEntry[labelModel] = i;
            booth.Children.Add(labelModel);

            _boothGroups.Add(booth);
            _root.Children.Add(booth);
        }

        // anello di selezione (accento)
        var ring = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(180, accent.R, accent.G, accent.B)));
        _focusRing = Box(1.7, 0.06, 1.1, 0, -100, 0, ring);
        _root.Children.Add(_focusRing);
    }

    private (ImageBrush brush, double aspect)? LoadBrush(Entry e)
    {
        try
        {
            using var bmp = ThumbnailProvider.GetThumbnail(e.FullPath, 256);
            if (bmp is null)
            {
                return null;
            }

            var aspect = bmp.Height == 0 ? 1.0 : (double)bmp.Width / bmp.Height;
            return (ToBrush(bmp), aspect);
        }
        catch
        {
            return null;
        }
    }

    // --- helper grafici ----------------------------------------------------

    private static ImageBrush ToBrush(Drawing.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, DrawingImaging.ImageFormat.Png);
        ms.Position = 0;
        var src = new BitmapImage();
        src.BeginInit();
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.StreamSource = ms;
        src.EndInit();
        src.Freeze();
        return new ImageBrush(src) { Stretch = Stretch.Uniform };
    }

    private static ImageBrush TiledBrush(Drawing.Bitmap tile, int repeat)
    {
        var b = ToBrush(tile);
        b.Stretch = Stretch.Fill;
        b.TileMode = TileMode.Tile;
        b.Viewport = new Rect(0, 0, 1.0 / repeat, 1.0 / repeat);
        b.ViewportUnits = BrushMappingMode.RelativeToBoundingBox;
        return b;
    }

    private static Drawing.Bitmap GridTile()
    {
        var bmp = new Drawing.Bitmap(128, 128);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.Clear(Drawing.Color.FromArgb(255, 18, 20, 28));
        using var pen = new Drawing.Pen(Drawing.Color.FromArgb(255, 40, 44, 60), 2);
        g.DrawRectangle(pen, 0, 0, 127, 127);
        return bmp;
    }

    private static ImageBrush TextBrush(string text, int fontPx, Drawing.Color bg, Drawing.Color fg, out double aspect)
    {
        using var font = new Drawing.Font("Segoe UI", fontPx, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        Drawing.SizeF size;
        using (var probe = new Drawing.Bitmap(1, 1))
        using (var pg = Drawing.Graphics.FromImage(probe))
        {
            size = pg.MeasureString(text, font);
        }

        var pad = fontPx * 0.5f;
        var w = (int)Math.Ceiling(size.Width + pad * 2);
        var h = (int)Math.Ceiling(size.Height + pad * 2);
        w = Math.Clamp(w, 32, 1024);
        aspect = (double)w / h;

        var bmp = new Drawing.Bitmap(w, h);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAlias;
            using var back = new Drawing.SolidBrush(bg);
            g.FillRectangle(back, 0, 0, w, h);
            using var fore = new Drawing.SolidBrush(fg);
            using var fmt = new Drawing.StringFormat
            {
                Alignment = Drawing.StringAlignment.Center,
                LineAlignment = Drawing.StringAlignment.Center
            };
            g.DrawString(text, font, fore, new Drawing.RectangleF(0, 0, w, h), fmt);
        }

        var brush = ToBrush(bmp);
        brush.Stretch = Stretch.Fill;
        bmp.Dispose();
        return brush;
    }

    private static GeometryModel3D Quad(double w, double h, double cx, double cy, double cz, Material mat, bool facePlusZ)
    {
        var hw = w / 2;
        var hh = h / 2;
        var mesh = new MeshGeometry3D();
        var z = 0.0;
        mesh.Positions.Add(new Point3D(-hw, hh, z));
        mesh.Positions.Add(new Point3D(hw, hh, z));
        mesh.Positions.Add(new Point3D(hw, -hh, z));
        mesh.Positions.Add(new Point3D(-hw, -hh, z));
        mesh.TextureCoordinates.Add(new Point(0, 0));
        mesh.TextureCoordinates.Add(new Point(1, 0));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(0, 1));
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(3);
        mesh.TriangleIndices.Add(2);
        return new GeometryModel3D(mesh, mat)
        {
            BackMaterial = mat,
            Transform = new TranslateTransform3D(cx, cy, cz)
        };
    }

    private static GeometryModel3D Box(double sx, double sy, double sz, double cx, double cy, double cz, Material mat)
    {
        var x = sx / 2;
        var y = sy / 2;
        var z = sz / 2;
        var mesh = new MeshGeometry3D();
        Point3D[] p =
        {
            new(-x, -y, -z), new(x, -y, -z), new(x, y, -z), new(-x, y, -z),
            new(-x, -y, z), new(x, -y, z), new(x, y, z), new(-x, y, z)
        };
        foreach (var pt in p)
        {
            mesh.Positions.Add(pt);
        }

        int[] idx =
        {
            0, 2, 1, 0, 3, 2, // fondo -Z
            4, 5, 6, 4, 6, 7, // fronte +Z
            0, 1, 5, 0, 5, 4, // basso
            3, 7, 6, 3, 6, 2, // alto
            1, 2, 6, 1, 6, 5, // destra
            0, 4, 7, 0, 7, 3  // sinistra
        };
        foreach (var i in idx)
        {
            mesh.TriangleIndices.Add(i);
        }

        return new GeometryModel3D(mesh, mat)
        {
            Transform = new TranslateTransform3D(cx, cy, cz)
        };
    }
}
