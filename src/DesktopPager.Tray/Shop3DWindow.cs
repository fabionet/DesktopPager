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
    private const int Columns = 6;
    private const double SpacingX = 3.2;
    private const double SpacingZ = 3.8;
    private const double MoveSpeed = 0.13;
    private const double TurnSpeed = 0.035; // radianti/tick

    private readonly PerspectiveCamera _camera = new() { FieldOfView = 55 };
    private readonly Model3DGroup _root = new();
    private readonly Viewport3D _viewport = new();
    private readonly DispatcherTimer _loop = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly HashSet<Key> _keys = new();

    private readonly TextBlock _pathLabel;
    private readonly TextBlock _focusLabel;

    private readonly List<Entry> _entries = new();
    private readonly List<Point3D> _boothPos = new();
    private readonly Dictionary<GeometryModel3D, int> _modelToEntry = new();
    private GeometryModel3D? _focusRing;

    private string? _current;    // null = "Questo PC"
    private int _focus = -1;
    private double _px, _pz, _yaw;
    private double _roomHalfW, _roomBackZ;

    // ingresso cinematografico
    private readonly Grid _rootGrid = new();
    private readonly Grid _loadingOverlay = new();
    private readonly Canvas _doorCanvas = new();
    private readonly WpfRectangle[] _doorPanes = new WpfRectangle[4];
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
        _rootGrid.Children.Add(_doorCanvas);
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

    private void BuildIntroOverlays()
    {
        // porta bianca stile logo Windows: 4 pannelli con una sottile fuga
        _doorCanvas.Background = System.Windows.Media.Brushes.Transparent;
        _doorCanvas.Visibility = Visibility.Collapsed;
        for (var i = 0; i < 4; i++)
        {
            var pane = new WpfRectangle
            {
                Fill = System.Windows.Media.Brushes.White,
                RenderTransform = new TranslateTransform()
            };
            _doorPanes[i] = pane;
            _doorCanvas.Children.Add(pane);
        }

        // schermata di caricamento
        _loadingOverlay.Background = new SolidColorBrush(Color.FromRgb(9, 13, 24));
        _loadingOverlay.Visibility = Visibility.Collapsed;
        var spinner = new WpfPath
        {
            Stroke = new SolidColorBrush(Color.FromRgb(120, 180, 255)),
            StrokeThickness = 6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 72,
            Height = 72,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = new PathGeometry(new[]
            {
                new PathFigure(new Point(60, 36), new[]
                {
                    new ArcSegment(new Point(12, 36), new System.Windows.Size(24, 24), 0, true, SweepDirection.Clockwise, true)
                }, false)
            }),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var spin = new RotateTransform();
        spinner.RenderTransform = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
        {
            RepeatBehavior = RepeatBehavior.Forever
        });
        var loadText = new TextBlock
        {
            Text = "Caricamento…",
            Foreground = System.Windows.Media.Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 110, 0, 0)
        };
        _loadingOverlay.Children.Add(spinner);
        _loadingOverlay.Children.Add(loadText);
    }

    private void PlayIntro()
    {
        _introDone = false;
        var w = ActualWidth;
        var h = ActualHeight;

        // camera in vista aerea sopra i banchi, guardando in basso
        _camera.Position = new Point3D(0, 15, -1);
        _camera.LookDirection = new Vector3D(0, -1, -0.18);

        // prepara i 4 pannelli della porta (2x2) partendo sopra lo schermo
        var halfW = w / 2;
        var halfH = h / 2;
        var gap = 3.0;
        var geo = new[] { (0.0, 0.0), (halfW + gap, 0.0), (0.0, halfH + gap), (halfW + gap, halfH + gap) };
        for (var i = 0; i < 4; i++)
        {
            var pane = _doorPanes[i];
            pane.Width = halfW - gap;
            pane.Height = halfH - gap;
            Canvas.SetLeft(pane, geo[i].Item1);
            Canvas.SetTop(pane, geo[i].Item2);
            pane.Opacity = 1;
            ((TranslateTransform)pane.RenderTransform).X = 0;
            ((TranslateTransform)pane.RenderTransform).Y = -h; // fuori schermo in alto
        }

        _loadingOverlay.Opacity = 1;
        _loadingOverlay.Visibility = Visibility.Visible;
        _doorCanvas.Visibility = Visibility.Collapsed;

        // fase 1: caricamento (~1.1s)
        After(1100, () =>
        {
            _loadingOverlay.BeginAnimation(OpacityProperty, Fade(1, 0, 300));
            After(300, () => _loadingOverlay.Visibility = Visibility.Collapsed);

            // fase 2: la porta cade dall'alto e si compone (~0.55s)
            _doorCanvas.Visibility = Visibility.Visible;
            foreach (var pane in _doorPanes)
            {
                ((TranslateTransform)pane.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-h, 0, TimeSpan.FromMilliseconds(550))
                    {
                        EasingFunction = new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut }
                    });
            }

            // fase 3: la porta si apre in 4 parti + atterraggio della camera
            After(700, () =>
            {
                OpenDoors(halfW, halfH);
                LandCamera();
                After(2050, FinishIntro);
            });
        });
    }

    private void OpenDoors(double halfW, double halfH)
    {
        var dur = TimeSpan.FromMilliseconds(1000);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        (double dx, double dy)[] dir = { (-halfW, -halfH), (halfW, -halfH), (-halfW, halfH), (halfW, halfH) };
        for (var i = 0; i < 4; i++)
        {
            var t = (TranslateTransform)_doorPanes[i].RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dir[i].dx, dur) { EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dir[i].dy, dur) { EasingFunction = ease });
            _doorPanes[i].BeginAnimation(OpacityProperty, Fade(1, 0, 1000));
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
        // libera la camera dalle animazioni e passa al controllo manuale
        _camera.BeginAnimation(PerspectiveCamera.PositionProperty, null);
        _camera.BeginAnimation(PerspectiveCamera.LookDirectionProperty, null);
        _doorCanvas.Visibility = Visibility.Collapsed;
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

        UpdateFocus();
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

            // piedistallo
            var pedestal = Box(1.5, 0.9, 0.7, x, 0.45, z, pedestalMat);
            _modelToEntry[pedestal] = i;
            _root.Children.Add(pedestal);

            // pannello con anteprima/icona
            var img = LoadBrush(_entries[i]);
            if (img is not null)
            {
                var w = 1.5 * Math.Clamp(img.Value.aspect, 0.5, 1.8);
                var panel = Quad(w, 1.5, x, 1.85, z + 0.36, new EmissiveMaterial(img.Value.brush), facePlusZ: true);
                _modelToEntry[panel] = i;
                _root.Children.Add(panel);
            }

            // etichetta col nome
            var label = TextBrush(_entries[i].Name, 26,
                Drawing.Color.FromArgb(220, 16, 18, 28), Drawing.Color.White, out var aspect);
            var labelModel = Quad(1.7, 1.7 / aspect, x, 2.95, z + 0.36, new EmissiveMaterial(label), facePlusZ: true);
            _modelToEntry[labelModel] = i;
            _root.Children.Add(labelModel);
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
