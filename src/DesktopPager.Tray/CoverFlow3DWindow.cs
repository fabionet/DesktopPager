using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DrawingImaging = System.Drawing.Imaging;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
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
/// Vista 3D reale (WPF Viewport3D) delle anteprime dei file: cover flow con
/// camera prospettica, copertine texturizzate inclinate nello spazio,
/// riflesso a pavimento e transizioni animate. Accelerata dalla GPU con
/// fallback software di WPF. A schermo intero.
/// </summary>
public sealed class CoverFlow3DWindow : Window
{
    private const int VisibleRange = 5;      // copertine per lato
    private const int MaxFiles = 400;
    private const int ThumbPx = 512;

    private const double CoverHeight = 1.5;
    private const double CenterSpacing = 1.3;
    private const double SideSpacing = 0.72;
    private const double Depth = 1.9;
    private const double FlipAngle = 66;      // gradi di inclinazione laterale

    private readonly List<string> _files = new();
    private readonly Dictionary<int, GeometryModel3D> _covers = new();
    private readonly Dictionary<int, GeometryModel3D> _reflections = new();
    private readonly Dictionary<int, double> _aspect = new();

    private readonly Model3DGroup _root = new();
    private readonly Viewport3D _viewport = new();
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly TextBlock _name;
    private readonly TextBlock _counter;

    private double _offset;   // posizione corrente (animata)
    private int _target;      // indice bersaglio

    public CoverFlow3DWindow(string folder)
    {
        Title = "DesktopPager3D-OS - Vista 3D";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Background = new SolidColorBrush(Color.FromRgb(8, 8, 12));
        ShowInTaskbar = false;
        Topmost = true;

        try
        {
            _files.AddRange(Directory.EnumerateFiles(folder)
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .Take(MaxFiles));
        }
        catch
        {
            // cartella non leggibile
        }

        // --- scena 3D ---
        var camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0.15, 3.6),
            LookDirection = new Vector3D(0, -0.05, -1),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 52
        };
        _viewport.Camera = camera;

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(255, 255, 255)));
        _root.Children.Add(lights);

        _viewport.Children.Add(new ModelVisual3D { Content = _root });

        // --- overlay testo ---
        _name = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 1100,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9
            }
        };
        _counter = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 200)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var caption = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 60)
        };
        caption.Children.Add(_name);
        caption.Children.Add(_counter);

        var grid = new Grid();
        grid.Children.Add(_viewport);
        grid.Children.Add(caption);
        Content = grid;

        _anim.Tick += (_, _) => AnimateStep();

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            RebuildScene();
            UpdateCaption();
        };
        KeyDown += OnKey;
        MouseWheel += (_, e) => MoveTo(_target + (e.Delta > 0 ? -1 : +1));
        MouseLeftButtonUp += OnClick;
        MouseDoubleClick += (_, _) => OpenCurrent();
    }

    // --- input -----------------------------------------------------------

    private void OnKey(object sender, WpfKeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Left: MoveTo(_target - 1); break;
            case Key.Right: MoveTo(_target + 1); break;
            case Key.Home: MoveTo(0); break;
            case Key.End: MoveTo(_files.Count - 1); break;
            case Key.Enter: OpenCurrent(); break;
        }
    }

    private void OnClick(object sender, WpfMouseButtonEventArgs e)
    {
        var cx = ActualWidth / 2;
        var x = e.GetPosition(this).X;
        if (Math.Abs(x - cx) < 120)
        {
            OpenCurrent();
        }
        else
        {
            MoveTo(_target + (x > cx ? +1 : -1));
        }
    }

    private void MoveTo(int index)
    {
        var clamped = Math.Clamp(index, 0, Math.Max(0, _files.Count - 1));
        if (clamped != _target)
        {
            _target = clamped;
            UpdateCaption();
            if (!_anim.IsEnabled)
            {
                _anim.Start();
            }
        }
    }

    private void AnimateStep()
    {
        var diff = _target - _offset;
        if (Math.Abs(diff) < 0.001)
        {
            _offset = _target;
            _anim.Stop();
        }
        else
        {
            _offset += diff * 0.22; // easing esponenziale
        }
        RebuildScene();
    }

    private void OpenCurrent()
    {
        if (_target < 0 || _target >= _files.Count)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = _files[_target], UseShellExecute = true });
        }
        catch
        {
            // apertura fallita
        }
    }

    private void UpdateCaption()
    {
        if (_files.Count == 0)
        {
            _name.Text = "Nessun file nella cartella";
            _counter.Text = "";
            return;
        }

        _name.Text = Path.GetFileName(_files[_target]);
        _counter.Text = $"{_target + 1} di {_files.Count}   —   frecce/rotella = scorri, Invio = apri, Esc = chiudi";
    }

    // --- costruzione scena ------------------------------------------------

    private void RebuildScene()
    {
        // mantieni solo le luci (primo figlio), poi ricomponi le copertine
        while (_root.Children.Count > 1)
        {
            _root.Children.RemoveAt(_root.Children.Count - 1);
        }

        var lo = Math.Max(0, (int)Math.Floor(_offset) - VisibleRange);
        var hi = Math.Min(_files.Count - 1, (int)Math.Ceiling(_offset) + VisibleRange);

        // disegna dalle piu' lontane alle piu' vicine (pittore)
        var order = new List<int>();
        for (var i = lo; i <= hi; i++)
        {
            order.Add(i);
        }
        order.Sort((a, b) => Math.Abs(b - _offset).CompareTo(Math.Abs(a - _offset)));

        foreach (var i in order)
        {
            var cover = GetCover(i);
            var refl = GetReflection(i);
            if (cover is null)
            {
                continue;
            }

            var transform = SlotTransform(i - _offset);
            cover.Transform = transform;
            _root.Children.Add(cover);
            if (refl is not null)
            {
                refl.Transform = transform;
                _root.Children.Add(refl);
            }
        }
    }

    private Transform3D SlotTransform(double rel)
    {
        var absR = Math.Abs(rel);
        var sign = rel < 0 ? -1.0 : 1.0;
        var near = Math.Min(1.0, absR);

        double x = absR <= 1
            ? rel * CenterSpacing
            : sign * CenterSpacing + (rel - sign) * SideSpacing;
        double z = -near * Depth;
        double tilt = -sign * FlipAngle * near; // le copertine laterali si girano verso il centro

        var g = new Transform3DGroup();
        g.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), tilt)));
        g.Children.Add(new TranslateTransform3D(x, 0, z));
        return g;
    }

    private GeometryModel3D? GetCover(int i)
    {
        if (_covers.TryGetValue(i, out var m))
        {
            return m;
        }

        var brush = LoadBrush(i, out var aspect);
        if (brush is null)
        {
            return null;
        }

        _aspect[i] = aspect;
        var mesh = QuadMesh(aspect, reflection: false);
        var mat = new DiffuseMaterial(brush);
        m = new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        _covers[i] = m;
        return m;
    }

    private GeometryModel3D? GetReflection(int i)
    {
        if (_reflections.TryGetValue(i, out var m))
        {
            return m;
        }

        var brush = LoadBrush(i, out var aspect);
        if (brush is null)
        {
            return null;
        }

        var rb = brush.Clone();
        rb.Opacity = 0.28;
        var mesh = QuadMesh(aspect, reflection: true);
        var mat = new DiffuseMaterial(rb);
        m = new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        _reflections[i] = m;
        return m;
    }

    private ImageBrush? LoadBrush(int i, out double aspect)
    {
        aspect = 1.0;
        if (i < 0 || i >= _files.Count)
        {
            return null;
        }

        try
        {
            using var bmp = ThumbnailProvider.GetThumbnail(_files[i], ThumbPx);
            if (bmp is null)
            {
                return null;
            }

            aspect = bmp.Height == 0 ? 1.0 : (double)bmp.Width / bmp.Height;
            using var ms = new MemoryStream();
            bmp.Save(ms, DrawingImaging.ImageFormat.Png);
            ms.Position = 0;
            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.StreamSource = ms;
            src.EndInit();
            src.Freeze();
            return new ImageBrush(src) { Stretch = Stretch.Fill };
        }
        catch
        {
            return null;
        }
    }

    private static MeshGeometry3D QuadMesh(double aspect, bool reflection)
    {
        var w = CoverHeight * Math.Clamp(aspect, 0.4, 2.6) / 2;
        var h = CoverHeight / 2;

        var mesh = new MeshGeometry3D();
        if (!reflection)
        {
            mesh.Positions.Add(new Point3D(-w, h, 0));
            mesh.Positions.Add(new Point3D(w, h, 0));
            mesh.Positions.Add(new Point3D(w, -h, 0));
            mesh.Positions.Add(new Point3D(-w, -h, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));
            mesh.TextureCoordinates.Add(new Point(1, 0));
            mesh.TextureCoordinates.Add(new Point(1, 1));
            mesh.TextureCoordinates.Add(new Point(0, 1));
        }
        else
        {
            // sotto la copertina, specchiata verticalmente (V invertita)
            mesh.Positions.Add(new Point3D(-w, -h, 0));
            mesh.Positions.Add(new Point3D(w, -h, 0));
            mesh.Positions.Add(new Point3D(w, -3 * h, 0));
            mesh.Positions.Add(new Point3D(-w, -3 * h, 0));
            mesh.TextureCoordinates.Add(new Point(0, 1));
            mesh.TextureCoordinates.Add(new Point(1, 1));
            mesh.TextureCoordinates.Add(new Point(1, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));
        }

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(3);
        mesh.TriangleIndices.Add(2);
        return mesh;
    }
}
