using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfImage = System.Windows.Controls.Image;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;

namespace DesktopPager.Tray.DesktopEffects;

/// <summary>
/// Finestra overlay a schermo intero, trasparente e "click-through", che mostra
/// un'istantanea della finestra trascinata e la fa ondeggiare come gomma: la
/// posizione insegue quella reale con una molla (overshoot elastico) e la
/// velocità genera una deformazione a taglio (shear) + schiacciamento, così
/// sembra gelatina. Il contenuto è un fermo-immagine durante il movimento.
/// </summary>
internal sealed class WobbleOverlay : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    // molla
    private const double Stiffness = 0.24;
    private const double Damping = 0.74;

    private readonly Canvas _canvas = new();
    private readonly WpfImage _image = new();
    private readonly SkewTransform _skew = new();
    private readonly ScaleTransform _scale = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private double _dipScale = 1.0;
    private double _wPx, _hPx;             // dimensione istantanea (px)
    private double _px, _py, _vx, _vy;     // posizione/velocità molla (px, top-left)
    private double _tx, _ty;               // bersaglio (px, top-left) = finestra reale
    private bool _settling;
    private int _settleTicks;

    public bool Active { get; private set; }

    /// <summary>Aggiorna la posizione bersaglio (angolo alto-sinistra della finestra reale).</summary>
    public void SetTarget(double leftPx, double topPx)
    {
        _tx = leftPx;
        _ty = topPx;
    }

    public WobbleOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;

        _image.Stretch = Stretch.Fill;
        _image.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        _image.RenderTransform = new TransformGroup { Children = { _scale, _skew } };
        _canvas.Children.Add(_image);
        Content = _canvas;

        _timer.Tick += (_, _) => Step();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle,
            ex | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget is not null)
        {
            _dipScale = src.CompositionTarget.TransformFromDevice.M11; // dip per px
        }
    }

    /// <summary>Avvia l'effetto per una finestra: istantanea + rettangolo iniziale.</summary>
    public void Begin(BitmapSource snapshot, EffectsNative.RECT startPx)
    {
        _wPx = Math.Max(1, startPx.Right - startPx.Left);
        _hPx = Math.Max(1, startPx.Bottom - startPx.Top);
        _px = _tx = startPx.Left;
        _py = _ty = startPx.Top;
        _vx = _vy = 0;
        _settling = false;

        // copre l'intero desktop virtuale, in DIP
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _image.Source = snapshot;
        _image.Width = _wPx * _dipScale;
        _image.Height = _hPx * _dipScale;

        Active = true;
        if (!IsVisible)
        {
            Show();
        }
        UpdateVisual();
        _timer.Start();
    }

    /// <summary>Fine trascinamento: la molla si assesta e poi l'overlay sparisce.</summary>
    public void Settle()
    {
        _settling = true;
        _settleTicks = 0;
    }

    private void Step()
    {
        // molla per asse (px)
        _vx = (_vx + (_tx - _px) * Stiffness) * Damping;
        _vy = (_vy + (_ty - _py) * Stiffness) * Damping;
        _px += _vx;
        _py += _vy;

        UpdateVisual();

        if (_settling)
        {
            _settleTicks++;
            var atRest = Math.Abs(_vx) < 0.4 && Math.Abs(_vy) < 0.4
                         && Math.Abs(_tx - _px) < 0.6 && Math.Abs(_ty - _py) < 0.6;
            if (atRest || _settleTicks > 90)
            {
                Hide();
                Active = false;
                _timer.Stop();
                _image.Source = null;
            }
        }
    }

    private void UpdateVisual()
    {
        // shear proporzionale alla velocità (gradi), limitato
        _skew.AngleX = Clamp(-_vx * 0.55, -16, 16);
        _skew.AngleY = Clamp(-_vy * 0.55, -16, 16);

        // schiacciamento nella direzione del moto
        var speed = Math.Sqrt(_vx * _vx + _vy * _vy);
        var squash = Clamp(speed * 0.004, 0, 0.10);
        _scale.ScaleX = 1 + (Math.Abs(_vx) >= Math.Abs(_vy) ? squash : -squash * 0.6);
        _scale.ScaleY = 1 + (Math.Abs(_vy) > Math.Abs(_vx) ? squash : -squash * 0.6);

        var canvasX = _px * _dipScale - SystemParameters.VirtualScreenLeft;
        var canvasY = _py * _dipScale - SystemParameters.VirtualScreenTop;
        Canvas.SetLeft(_image, canvasX);
        Canvas.SetTop(_image, canvasY);
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
