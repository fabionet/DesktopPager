using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DesktopPager.Tray;

/// <summary>
/// Moneta: pulsante rotondo trasparente che, al clic, viene "lanciata" — sale
/// in un arco girando su sé stessa e rallenta atterrando, come una moneta vera.
/// L'animazione va a tempo reale (Stopwatch) e non a conteggio di fotogrammi,
/// così resta fluida anche se il timer perde qualche colpo.
/// </summary>
public sealed class CoinButton : Control
{
    private const int DurationMs = 900;   // durata del lancio
    private const double Turns = 3.0;     // giri completi

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();

    private double _angle;   // rotazione, in radianti
    private double _lift;    // 0..1: quanto è "in aria" (rimpicciolisce, non sale:
                             // il controllo ritaglia ai propri bordi e l'arco
                             // verrebbe tagliato)
    private bool _animating;
    private bool _fired;
    private Image? _coin;

    /// <summary>Scattato appena la moneta è di taglio la prima volta.</summary>
    public event Action? Activated;

    public CoinButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = false;
        _timer.Tick += (_, _) => Step();
    }

    public Image? Coin
    {
        get => _coin;
        set { _coin = value; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            Toss();
        }
    }

    private void Toss()
    {
        if (_animating)
        {
            return;
        }

        _animating = true;
        _fired = false;
        _angle = 0;
        _lift = 0;
        _clock.Restart();
        _timer.Start();
    }

    private void Step()
    {
        var t = _clock.ElapsedMilliseconds / (double)DurationMs;
        if (t >= 1)
        {
            // atterrata: dritta e ferma
            _animating = false;
            _angle = 0;
            _lift = 0;
            _timer.Stop();
            _clock.Stop();
            Invalidate();
            Fire();
            return;
        }

        // ease-out cubica: parte veloce e rallenta, come un lancio che si esaurisce
        var eased = 1 - Math.Pow(1 - t, 3);
        _angle = eased * Turns * 2 * Math.PI;

        // arco del lancio: si allontana e ritorna (mezza sinusoide)
        _lift = Math.Sin(t * Math.PI);

        // appena è di taglio la prima volta apri il menu: la moneta finisce di
        // girare mentre il menu compare
        if (!_fired && _angle >= Math.PI / 2)
        {
            Fire();
        }

        Invalidate();
    }

    private void Fire()
    {
        if (_fired)
        {
            return;
        }

        _fired = true;
        Activated?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_coin is null)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // in aria rimpicciolisce (si allontana), girando si assottiglia fino a
        // sparire di taglio
        var scale = 1 - 0.18 * _lift;
        var cos = Math.Cos(_angle);
        var h = (float)(Height * scale);
        var w = (float)(Height * scale * Math.Abs(cos)); // Height: la moneta resta tonda
        if (w < 1f)
        {
            w = 1f;
        }

        var x = (Width - w) / 2f;
        var y = (Height - h) / 2f;
        g.DrawImage(_coin, x, y, w, h);

        // mezzo giro dopo si vede il rovescio: stessa sagoma, in ombra
        if (_animating && cos < 0)
        {
            using var veil = new SolidBrush(Color.FromArgb(130, 18, 14, 22));
            g.FillEllipse(veil, x, y, w, h);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
