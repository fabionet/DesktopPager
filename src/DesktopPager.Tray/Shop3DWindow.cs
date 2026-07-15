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
using WpfPolygon = System.Windows.Shapes.Polygon;
using WpfPath = System.Windows.Shapes.Path;
using WpfImage = System.Windows.Controls.Image;
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
    // Stanza in stile labirinto: corridoio centrale libero fino all'uscita in
    // fondo, le cartelle sono porte-cubo incassate nelle pareti (una schiera a
    // sinistra e una a destra) e i file sono due schiere sospese davanti alle
    // pareti. Niente piedistalli.
    private const double FileLaneX = 4.2;   // corsia dei file
    private const double CubeInnerX = 6.2;  // faccia della cartella-cubo verso il corridoio
    private const double WallX = 7.4;       // pareti laterali
    private const double DoorW = 2.6;       // larghezza della porta di una cartella
    private const double DoorH = 3.0;       // altezza
    private const double DoorOpenStart = 5.0; // distanza a cui la porta inizia ad aprirsi
    private const double DoorOpenFull = 2.4;  // distanza a cui è spalancata
    private const double DoorEnter = 1.7;     // si entra nella cartella
    private const double SpacingZ = 4.8;   // passo delle schiere lungo il corridoio
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
    // parallelo a _entries; null per le cartelle (le porte non si rimpiccioliscono)
    private readonly List<Model3DGroup?> _boothGroups = new();

    /// <summary>Porta di una cartella: due ante che scorrono lungo la parete.</summary>
    private sealed class FolderDoor
    {
        public int Entry;
        public double X, Z;
        public Model3DGroup Near = null!, Far = null!;
    }

    private readonly List<FolderDoor> _folderDoors = new();

    // Anteprime caricate in sottofondo: la shell può metterci più di un secondo
    // per UNA cartella (compone il contenuto nell'icona), quindi la stanza si
    // costruisce subito con un segnaposto e le immagini arrivano dopo.
    private readonly List<(int Entry, GeometryModel3D Model)> _pendingThumbs = new();
    private int _thumbGeneration; // cambia a ogni stanza: annulla i caricamenti vecchi
    private readonly Dictionary<GeometryModel3D, int> _modelToEntry = new();
    private GeometryModel3D? _focusRing;

    private string? _current;    // null = "Questo PC"
    private int _focus = -1;
    private double _px, _pz, _yaw;
    private double _lastPx, _lastPz, _lastYaw; // ultimo stato applicato: da fermi si salta il giro
    private bool _settled;
    private double _roomHalfW, _roomBackZ;

    // porta di uscita: un tesseratto sulla parete di fondo che si apre
    // avvicinandosi, oltre il quale si vede il desktop; entrandoci parte uno
    // zoom che riporta al desktop reale
    private const double ExitDoorSize = 3.2;
    private const double ExitOpenStart = 7.0;  // distanza a cui inizia ad aprirsi
    private const double ExitOpenFull = 3.0;   // distanza a cui è spalancata
    private const double ExitTrigger = 2.0;    // si attraversa il varco

    private BitmapSource? _desktopShot;
    private readonly Model3DGroup?[] _exitPanels = new Model3DGroup?[4];
    private Model3DGroup? _exitCore;
    private readonly WpfImage _exitZoom = new();
    private double _exitDoorZ, _exitBaseY;
    private bool _exiting;

    // ingresso cinematografico: schermo nero, tesseratto (ipercubo) colorato
    // che si apre come una porta, con barra di avanzamento del caricamento
    private readonly Grid _rootGrid = new();
    private readonly Grid _loadingOverlay = new();
    private readonly Canvas _logoCanvas = new();
    private readonly WpfPolygon[] _logoPanes = new WpfPolygon[4]; // facce trapezoidali
    private WpfPolygon? _logoCore;                                 // cubo interno
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
        hint.Text = "frecce / WASD = muoviti e gira   •   Invio = entra o apri   •   Backspace = indietro   •   "
                    + "porta in fondo o Esc = torna al desktop";

        // istantanea del desktop PRIMA che questa finestra lo copra: è ciò che
        // si vedrà oltre la porta di uscita e nello zoom finale
        try
        {
            _desktopShot = DesktopEffects.Capture.Screen(0, 0,
                Math.Max(1, (int)SystemParameters.PrimaryScreenWidth),
                Math.Max(1, (int)SystemParameters.PrimaryScreenHeight));
        }
        catch
        {
            _desktopShot = null; // senza istantanea la porta resta chiusa: si esce con Esc
        }

        _exitZoom.Stretch = Stretch.Fill;
        _exitZoom.Visibility = Visibility.Collapsed;
        _exitZoom.IsHitTestVisible = false;

        _rootGrid.Children.Add(_viewport);
        _rootGrid.Children.Add(_pathLabel);
        _rootGrid.Children.Add(_focusLabel);
        _rootGrid.Children.Add(hint);
        BuildIntroOverlays();
        _rootGrid.Children.Add(_loadingOverlay);
        _rootGrid.Children.Add(_exitZoom);
        Content = _rootGrid;

        Focusable = true;
        _loop.Tick += (_, _) => Tick();
        Loaded += (_, _) => { GrabFocus(); Navigate(null); PlayIntro(); };
        // PreviewKeyDown: i tasti arrivano comunque, anche se il focus finisce
        // su un elemento figlio
        PreviewKeyDown += OnKeyDown;
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

    private const double LogoSize = 178;   // lato del quadrato esterno del tesseratto
    private const double LogoInset = 50;   // rientro del cubo interno

    private static Color Rgb((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);

    private void BuildIntroOverlays()
    {
        // overlay a schermo nero
        _loadingOverlay.Background = System.Windows.Media.Brushes.Black;
        _loadingOverlay.Visibility = Visibility.Collapsed;

        // tesseratto (ipercubo) colorato, centrato: cubo esterno + cubo interno
        // + spigoli di collegamento. Le 4 facce trapezoidali sono i battenti
        // della "porta" che si apre.
        _logoCanvas.Width = LogoSize;
        _logoCanvas.Height = LogoSize;
        _logoCanvas.HorizontalAlignment = HorizontalAlignment.Center;
        _logoCanvas.VerticalAlignment = VerticalAlignment.Center;
        _logoCanvas.Margin = new Thickness(0, 0, 0, 90);

        const double s = LogoSize;
        const double m = LogoInset;
        var oTL = new Point(0, 0);
        var oTR = new Point(s, 0);
        var oBR = new Point(s, s);
        var oBL = new Point(0, s);
        var iTL = new Point(m, m);
        var iTR = new Point(s - m, m);
        var iBR = new Point(s - m, s - m);
        var iBL = new Point(m, s - m);

        // su fondo nero gli spigoli chiari fanno risaltare il reticolo
        var edge = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xFF));

        (Point[] pts, Color col)[] faces =
        {
            (new[] { oTL, oTR, iTR, iTL }, Rgb(TesseractPalette.Top)),
            (new[] { oTR, oBR, iBR, iTR }, Rgb(TesseractPalette.Right)),
            (new[] { oBR, oBL, iBL, iBR }, Rgb(TesseractPalette.Bottom)),
            (new[] { oBL, oTL, iTL, iBL }, Rgb(TesseractPalette.Left))
        };
        for (var i = 0; i < 4; i++)
        {
            var pane = new WpfPolygon
            {
                Points = new PointCollection(faces[i].pts),
                Fill = new SolidColorBrush(faces[i].col),
                Stroke = edge,
                StrokeThickness = 2.5,
                RenderTransform = new TranslateTransform()
            };
            _logoPanes[i] = pane;
            _logoCanvas.Children.Add(pane);
        }

        // cubo interno: resta fermo mentre i battenti si aprono, poi vola
        // verso lo spettatore (si attraversa l'ipercubo)
        _logoCore = new WpfPolygon
        {
            Points = new PointCollection(new[] { iTL, iTR, iBR, iBL }),
            Fill = new SolidColorBrush(Rgb(TesseractPalette.Core)),
            Stroke = edge,
            StrokeThickness = 2.5,
            RenderTransform = new ScaleTransform(1, 1, s / 2, s / 2)
        };
        _logoCanvas.Children.Add(_logoCore);

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

        // reset tesseratto e barra
        foreach (var pane in _logoPanes)
        {
            pane.Opacity = 1;
            var t = (TranslateTransform)pane.RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
            t.X = 0;
            t.Y = 0;
        }
        if (_logoCore is not null)
        {
            _logoCore.Opacity = 1;
            _logoCore.BeginAnimation(OpacityProperty, null);
            var cs = (ScaleTransform)_logoCore.RenderTransform;
            cs.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            cs.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            cs.ScaleX = 1;
            cs.ScaleY = 1;
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

        // ogni faccia trapezoidale esce dal proprio lato (alto/destra/basso/sinistra)
        (double dx, double dy)[] dir =
        {
            (0, -spread), (spread, 0), (0, spread), (-spread, 0)
        };
        for (var i = 0; i < 4; i++)
        {
            var t = (TranslateTransform)_logoPanes[i].RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dir[i].dx, dur) { EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dir[i].dy, dur) { EasingFunction = ease });
        }

        // il cubo interno ingrandisce fino ad avvolgere lo spettatore e svanisce
        if (_logoCore is not null)
        {
            var cs = (ScaleTransform)_logoCore.RenderTransform;
            cs.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 9, dur) { EasingFunction = ease });
            cs.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 9, dur) { EasingFunction = ease });
            _logoCore.BeginAnimation(OpacityProperty, Fade(1, 0, 1300));
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

    /// <summary>
    /// Porta davvero il focus di tastiera a questa finestra: la barra e la tray
    /// sono tool-window sempre in primo piano e l'attivazione può restare a
    /// loro, lasciando la vista 3D senza tastiera (Esc compreso).
    /// </summary>
    private void GrabFocus()
    {
        try
        {
            Activate();
            Focus();
            System.Windows.Input.Keyboard.Focus(this);
        }
        catch
        {
            // se l'attivazione viene negata restano mouse e porta di uscita
        }
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_exiting) { return; }
            if (!_introDone) { FinishIntro(); return; } // salta l'intro
            Close();
            return;
        }

        if (_exiting)
        {
            return; // durante lo zoom di uscita i comandi sono bloccati
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
        if (!_introDone || _exiting)
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

        // Da fermi non cambia nulla: banchi, messa a fuoco e porte dipendono
        // solo dalla posizione. Saltare il giro evita di sporcare l'albero di
        // rendering 60 volte al secondo per niente.
        if (_settled && _px == _lastPx && _pz == _lastPz && _yaw == _lastYaw)
        {
            return;
        }

        _lastPx = _px;
        _lastPz = _pz;
        _lastYaw = _yaw;
        _settled = true;

        _camera.Position = new Point3D(_px, 1.65, _pz);
        _camera.LookDirection = new Vector3D(fwd.X, -0.08, fwd.Z);

        UpdateBoothScale();
        UpdateFocus();
        if (UpdateFolderDoors())
        {
            return; // siamo entrati in una cartella: la stanza è stata ricostruita
        }
        UpdateExitDoor();
    }

    /// <summary>
    /// Le porte delle cartelle si aprono man mano che ci si avvicina; arrivando
    /// sulla soglia si entra. Restituisce true se la stanza è cambiata.
    /// </summary>
    private bool UpdateFolderDoors()
    {
        foreach (var d in _folderDoors)
        {
            var dx = _px - d.X;
            var dz = _pz - d.Z;
            var dist = Math.Sqrt(dx * dx + dz * dz);

            double open;
            if (dist >= DoorOpenStart) open = 0;
            else if (dist <= DoorOpenFull) open = 1;
            else open = (DoorOpenStart - dist) / (DoorOpenStart - DoorOpenFull);

            var slide = open * (DoorW / 2 + 0.05);
            ((TranslateTransform3D)d.Near.Transform).OffsetZ = -slide;
            ((TranslateTransform3D)d.Far.Transform).OffsetZ = slide;

            if (dist <= DoorEnter && open >= 0.98)
            {
                Navigate(_entries[d.Entry].FullPath);
                return true; // _folderDoors è stata ricostruita: non continuare il ciclo
            }
        }

        return false;
    }

    /// <summary>
    /// La porta di uscita si apre man mano che ci si avvicina: le 4 facce
    /// scorrono ciascuna verso il proprio lato e il cubo interno si richiude,
    /// scoprendo il desktop. Attraversando il varco parte lo zoom di uscita.
    /// </summary>
    private void UpdateExitDoor()
    {
        if (_exitCore is null)
        {
            return;
        }

        var dx = _px;                 // la porta è centrata su x = 0
        var dz = _pz - _exitDoorZ;
        var dist = Math.Sqrt(dx * dx + dz * dz);

        double open;
        if (dist >= ExitOpenStart) open = 0;
        else if (dist <= ExitOpenFull) open = 1;
        else open = (ExitOpenStart - dist) / (ExitOpenStart - ExitOpenFull);

        // ogni faccia esce dal proprio lato, come nell'intro
        var slide = open * ExitDoorSize * 1.05;
        (double x, double y)[] dir = { (0, slide), (slide, 0), (0, -slide), (-slide, 0) };
        for (var i = 0; i < 4; i++)
        {
            if (_exitPanels[i] is { } panel)
            {
                panel.Transform = new TranslateTransform3D(dir[i].x, dir[i].y, 0);
            }
        }

        var cs = Math.Max(0.001, 1 - open);
        _exitCore.Transform = new ScaleTransform3D(cs, cs, 1, 0, _exitBaseY, _exitDoorZ);

        if (dist <= ExitTrigger && open >= 0.98)
        {
            StartExitZoom();
        }
    }

    /// <summary>Zoom sull'immagine del desktop, poi chiude: si torna al desktop reale.</summary>
    private void StartExitZoom()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        _keys.Clear();

        if (_desktopShot is null)
        {
            Close();
            return;
        }

        _exitZoom.Source = _desktopShot;
        _exitZoom.Opacity = 0;
        _exitZoom.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(0.3, 0.3);
        _exitZoom.RenderTransform = st;
        _exitZoom.Visibility = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var dur = TimeSpan.FromMilliseconds(700);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.3, 1, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, dur) { EasingFunction = ease });
        _exitZoom.BeginAnimation(OpacityProperty, Fade(0, 1, 400));
        After(780, Close);
    }

    // I banchi vicini si rimpiccioliscono per restare leggibili per intero.
    private void UpdateBoothScale()
    {
        for (var i = 0; i < _boothGroups.Count && i < _boothPos.Count; i++)
        {
            if (_boothGroups[i] is null)
            {
                continue; // porta di una cartella: non si rimpicciolisce
            }

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
        // un clic sulla vista riporta qui il focus: da qui in poi Esc funziona
        if (!IsKeyboardFocusWithin)
        {
            GrabFocus();
        }

        if (!_introDone || _exiting)
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
        _settled = false; // stanza nuova: il prossimo giro deve applicare tutto
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
        _folderDoors.Clear();

        // luci: ambiente tenue + direzionale per dare volume
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(96, 96, 104)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(210, 210, 220), new Vector3D(-0.4, -1, -0.5)));
        _root.Children.Add(lights);

        // cartelle -> porte nelle pareti; file -> schiere sospese. Ogni schiera
        // alterna sinistra/destra, così i due lati restano equilibrati.
        var folders = new List<int>();
        var files = new List<int>();
        for (var i = 0; i < _entries.Count; i++)
        {
            (_entries[i].IsContainer ? folders : files).Add(i);
        }

        var rows = Math.Max(1, Math.Max((folders.Count + 1) / 2, (files.Count + 1) / 2));
        _roomHalfW = WallX;
        _roomBackZ = -(2.5 + rows * SpacingZ) - 3.0;
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

        // _boothPos/_boothGroups restano paralleli a _entries (li usano messa a
        // fuoco e rimpicciolimento)
        var pos = new Point3D[_entries.Count];
        var groups = new Model3DGroup?[_entries.Count];

        for (var j = 0; j < folders.Count; j++)
        {
            var i = folders[j];
            var side = j % 2 == 0 ? -1 : 1;
            var z = -3.0 - j / 2 * SpacingZ;
            pos[i] = new Point3D(side * CubeInnerX, 0, z);
            groups[i] = null;                 // le porte non si rimpiccioliscono
            BuildFolderDoor(i, side, z);
        }

        for (var j = 0; j < files.Count; j++)
        {
            var i = files[j];
            var side = j % 2 == 0 ? -1 : 1;
            var x = side * FileLaneX;
            var z = -2.5 - j / 2 * SpacingZ;
            pos[i] = new Point3D(x, 0, z);
            groups[i] = BuildFilePanel(i, x, z);
        }

        _boothPos.AddRange(pos);
        _boothGroups.AddRange(groups);

        // anello di selezione (accento)
        var accent = ThemeService.Accent;
        var ring = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(180, accent.R, accent.G, accent.B)));
        _focusRing = Box(1.7, 0.06, 1.1, 0, -100, 0, ring);
        _root.Children.Add(_focusRing);

        BuildExitDoor();

        // le anteprime arrivano dopo: la stanza deve comparire subito
        StartThumbnailLoad();
    }

    /// <summary>File sospeso davanti alla parete: anteprima/icona + nome, senza piedistallo.</summary>
    private Model3DGroup BuildFilePanel(int i, double x, double z)
    {
        var g = new Model3DGroup();

        // Pannello quadrato con segnaposto: l'anteprima vera arriva da
        // StartThumbnailLoad e sostituisce solo il materiale, così la geometria
        // non dipende dalle proporzioni dell'immagine (che ancora non si sanno).
        // BackMaterial = null: visti da dietro mostrerebbero il testo specchiato.
        var panel = Quad(1.5, 1.5, x, 1.55, z, PlaceholderMaterial(), facePlusZ: true);
        panel.BackMaterial = null;
        _modelToEntry[panel] = i;
        g.Children.Add(panel);
        _pendingThumbs.Add((i, panel));

        var label = TextBrush(_entries[i].Name, 26,
            Drawing.Color.FromArgb(220, 16, 18, 28), Drawing.Color.White, out var aspect);
        var labelModel = Quad(1.7, 1.7 / aspect, x, 2.55, z, new EmissiveMaterial(label), facePlusZ: true);
        labelModel.BackMaterial = null;
        _modelToEntry[labelModel] = i;
        g.Children.Add(labelModel);

        _root.Children.Add(g);
        return g;
    }

    /// <summary>
    /// Cartella come cubo incassato nella parete, con due ante che scorrono via
    /// all'avvicinarsi (vedi UpdateFolderDoors) scoprendo l'interno luminoso.
    /// </summary>
    private void BuildFolderDoor(int i, int side, double z)
    {
        var faceX = side * CubeInnerX;
        var depth = WallX - CubeInnerX;

        // Il cubo della cartella è un PORTALE incassato, non un blocco pieno:
        // architrave e montanti attorno all'apertura. Con un Box solido la sua
        // faccia verso il corridoio coprirebbe ante e interno luminoso.
        var cubeMat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(52, 58, 78)));
        var cx = side * (CubeInnerX + depth / 2);
        var lintel = Box(depth, 0.5, DoorW + 0.6, cx, DoorH + 0.25, z, cubeMat);
        _modelToEntry[lintel] = i;
        _root.Children.Add(lintel);
        _root.Children.Add(Box(depth, DoorH + 0.5, 0.3, cx, (DoorH + 0.5) / 2, z - DoorW / 2 - 0.15, cubeMat));
        _root.Children.Add(Box(depth, DoorH + 0.5, 0.3, cx, (DoorH + 0.5) / 2, z + DoorW / 2 + 0.15, cubeMat));

        // interno luminoso, visibile quando le ante si aprono
        var glow = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(
            TesseractPalette.Core.R, TesseractPalette.Core.G, TesseractPalette.Core.B)));
        _root.Children.Add(WallQuad(side * (WallX - 0.15), 0.05, DoorH, z - DoorW / 2, z + DoorW / 2, glow));

        // due ante (colori del tesseratto) che scorrono lungo la parete
        var door = new FolderDoor { Entry = i, X = faceX, Z = z };
        door.Near = DoorPanel(faceX, z - DoorW / 2, z, TesseractPalette.Left, i);
        door.Far = DoorPanel(faceX, z, z + DoorW / 2, TesseractPalette.Right, i);
        _folderDoors.Add(door);

        // Nome sull'architrave, rivolto al corridoio: girandosi verso la porta
        // per entrarci si deve poter leggere di che cartella si tratta (un
        // pannello rivolto all'ingresso si vedrebbe di taglio).
        var label = TextBrush(_entries[i].Name, 26,
            Drawing.Color.FromArgb(220, 16, 18, 28), Drawing.Color.White, out var aspect);
        var lw = DoorW;
        var lh = Math.Min(0.42, lw / aspect);
        var labelModel = WallQuad(faceX - side * 0.03, DoorH + 0.05, DoorH + 0.05 + lh,
            z - lw / 2, z + lw / 2, new EmissiveMaterial(label), flipU: side < 0);
        _modelToEntry[labelModel] = i;
        _root.Children.Add(labelModel);
    }

    private Model3DGroup DoorPanel(double x, double z0, double z1, (byte R, byte G, byte B) col, int entry)
    {
        var model = WallQuad(x, 0.05, DoorH, z0, z1,
            new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(col.R, col.G, col.B))));
        _modelToEntry[model] = entry;

        var g = new Model3DGroup { Transform = new TranslateTransform3D(0, 0, 0) };
        g.Children.Add(model);
        _root.Children.Add(g);
        return g;
    }

    /// <summary>
    /// Quadrilatero sul piano YZ (parete laterale), visibile da entrambi i lati.
    /// flipU inverte la texture lungo z: guardando la parete di SINISTRA lo
    /// schermo scorre verso -z, quindi senza inversione il testo si legge
    /// specchiato (sulla destra invece va già bene).
    /// </summary>
    private static GeometryModel3D WallQuad(double x, double y0, double y1, double z0, double z1,
        Material mat, bool flipU = false)
    {
        var u0 = flipU ? 1.0 : 0.0;
        var u1 = flipU ? 0.0 : 1.0;
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(x, y1, z0));
        mesh.Positions.Add(new Point3D(x, y1, z1));
        mesh.Positions.Add(new Point3D(x, y0, z1));
        mesh.Positions.Add(new Point3D(x, y0, z0));
        mesh.TextureCoordinates.Add(new Point(u0, 0));
        mesh.TextureCoordinates.Add(new Point(u1, 0));
        mesh.TextureCoordinates.Add(new Point(u1, 1));
        mesh.TextureCoordinates.Add(new Point(u0, 1));
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(3);
        mesh.TriangleIndices.Add(2);
        mesh.Freeze(); // la geometria non cambia più: congelarla alleggerisce WPF
        return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
    }

    /// <summary>
    /// Porta di uscita a forma di tesseratto sulla parete di fondo: dietro c'è
    /// l'istantanea del desktop, davanti le 4 facce trapezoidali e il cubo
    /// interno che la coprono. Avvicinandosi le facce scorrono via e il cubo si
    /// richiude su sé stesso, scoprendo il desktop (vedi UpdateExitDoor).
    /// </summary>
    private void BuildExitDoor()
    {
        for (var i = 0; i < 4; i++)
        {
            _exitPanels[i] = null;
        }
        _exitCore = null;

        if (_desktopShot is null)
        {
            return; // senza istantanea niente porta: si esce con Esc
        }

        const double s = ExitDoorSize;
        const double half = s / 2;
        const double m = s * 0.28;
        _exitDoorZ = _roomBackZ + 0.3;
        _exitBaseY = half + 0.05;              // appoggiata al pavimento
        var y0 = _exitBaseY - half;
        var y1 = _exitBaseY + half;
        var z = _exitDoorZ;

        // il varco: il desktop, dietro al tesseratto. Emissivo così non viene
        // spento dalle luci della stanza; UniformToFill per non schiacciare il 16:9
        _root.Children.Add(Quad(s, s, 0, _exitBaseY, z - 0.04,
            new EmissiveMaterial(new ImageBrush(_desktopShot) { Stretch = Stretch.UniformToFill }),
            facePlusZ: true));

        Point3D P(double x, double y) => new(x, y, z);
        var oTL = P(-half, y1);
        var oTR = P(half, y1);
        var oBR = P(half, y0);
        var oBL = P(-half, y0);
        var iTL = P(-half + m, y1 - m);
        var iTR = P(half - m, y1 - m);
        var iBR = P(half - m, y0 + m);
        var iBL = P(-half + m, y0 + m);

        (Point3D a, Point3D b, Point3D c, Point3D d, (byte R, byte G, byte B) col)[] faces =
        {
            (oTL, oTR, iTR, iTL, TesseractPalette.Top),
            (oTR, oBR, iBR, iTR, TesseractPalette.Right),
            (oBR, oBL, iBL, iBR, TesseractPalette.Bottom),
            (oBL, oTL, iTL, iBL, TesseractPalette.Left)
        };
        // ATTENZIONE: le ante devono essere DiffuseMaterial (opache). Con
        // EmissiveMaterial si sommerebbero al desktop che sta dietro e i colori
        // uscirebbero falsati (rosso+blu = rosa, ecc.).
        for (var i = 0; i < 4; i++)
        {
            var f = faces[i];
            var panel = new Model3DGroup();
            panel.Children.Add(Poly4(f.a, f.b, f.c, f.d,
                new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(f.col.R, f.col.G, f.col.B)))));
            panel.Transform = new TranslateTransform3D(0, 0, 0);
            _exitPanels[i] = panel;
            _root.Children.Add(panel);
        }

        var core = new Model3DGroup();
        core.Children.Add(Poly4(iTL, iTR, iBR, iBL,
            new DiffuseMaterial(new SolidColorBrush(
                Color.FromRgb(TesseractPalette.Core.R, TesseractPalette.Core.G, TesseractPalette.Core.B)))));
        core.Transform = new ScaleTransform3D(1, 1, 1, 0, _exitBaseY, z);
        _exitCore = core;
        _root.Children.Add(core);
    }

    /// <summary>Quadrilatero 3D da 4 punti arbitrari (visibile da entrambi i lati).</summary>
    private static GeometryModel3D Poly4(Point3D a, Point3D b, Point3D c, Point3D d, Material mat)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(a);
        mesh.Positions.Add(b);
        mesh.Positions.Add(c);
        mesh.Positions.Add(d);
        // stesso avvolgimento di Quad(facePlusZ): normali verso +Z, così le
        // luci della stanza illuminano la faccia rivolta a chi guarda
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(3);
        mesh.TriangleIndices.Add(2);
        mesh.Freeze(); // la geometria non cambia più: congelarla alleggerisce WPF
        return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
    }

    /// <summary>Materiale neutro mostrato finché non arriva l'anteprima vera.</summary>
    private static Material PlaceholderMaterial()
    {
        var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(70, 90, 95, 115)));
        mat.Freeze();
        return mat;
    }

    /// <summary>
    /// Carica le anteprime fuori dal thread dell'interfaccia e le innesta man
    /// mano che arrivano. Per le CARTELLE chiede solo l'icona: farsi comporre
    /// l'anteprima del contenuto costa oltre un secondo l'una.
    /// </summary>
    private void StartThumbnailLoad()
    {
        if (_pendingThumbs.Count == 0)
        {
            return;
        }

        var token = ++_thumbGeneration;
        var jobs = _pendingThumbs
            .Select(p => (p.Model, _entries[p.Entry].FullPath, _entries[p.Entry].IsContainer))
            .ToArray();
        _pendingThumbs.Clear();

        Task.Run(() =>
        {
            foreach (var (model, path, isContainer) in jobs)
            {
                if (token != _thumbGeneration)
                {
                    return; // l'utente ha già cambiato stanza
                }

                BitmapSource? src = null;
                try
                {
                    var flags = isContainer
                        ? ThumbnailProvider.ThumbFlags.IconOnly
                        : ThumbnailProvider.ThumbFlags.ResizeToFit;
                    using var bmp = ThumbnailProvider.GetThumbnail(path, 256, flags);
                    if (bmp is not null)
                    {
                        src = ToBitmapSource(bmp); // già freezato: attraversa i thread
                    }
                }
                catch
                {
                    // anteprima non disponibile: resta il segnaposto
                }

                if (src is null)
                {
                    continue;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token != _thumbGeneration)
                    {
                        return;
                    }

                    // Uniform: il pannello è quadrato, l'immagine ci sta dentro
                    // senza deformarsi qualunque siano le sue proporzioni
                    var brush = new ImageBrush(src) { Stretch = Stretch.Uniform };
                    brush.Freeze();
                    var mat = new EmissiveMaterial(brush);
                    mat.Freeze();
                    model.Material = mat;
                }), DispatcherPriority.Background);
            }
        });
    }

    // --- helper grafici ----------------------------------------------------

    /// <summary>Bitmap GDI+ -> BitmapSource freezato (si può creare fuori dal thread UI).</summary>
    private static BitmapSource ToBitmapSource(Drawing.Bitmap bmp)
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
        return src;
    }

    private static ImageBrush ToBrush(Drawing.Bitmap bmp)
    {
        var b = new ImageBrush(ToBitmapSource(bmp)) { Stretch = Stretch.Uniform };
        b.Freeze();
        return b;
    }

    private static ImageBrush TiledBrush(Drawing.Bitmap tile, int repeat)
    {
        // costruito e poi congelato: ToBrush restituisce un pennello già
        // congelato, che non si potrebbe più modificare
        var b = new ImageBrush(ToBitmapSource(tile))
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 1.0 / repeat, 1.0 / repeat),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox
        };
        b.Freeze();
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

        // costruito e congelato in un colpo: ToBrush lo restituirebbe già
        // congelato e Stretch non sarebbe più modificabile
        var brush = new ImageBrush(ToBitmapSource(bmp)) { Stretch = Stretch.Fill };
        brush.Freeze();
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
        mesh.Freeze();
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

        mesh.Freeze();
        return new GeometryModel3D(mesh, mat)
        {
            Transform = new TranslateTransform3D(cx, cy, cz)
        };
    }
}
