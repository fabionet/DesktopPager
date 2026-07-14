using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DesktopPager.Tray;

/// <summary>
/// Moneta Windows: pulsante rotondo trasparente che, al clic, fa una breve
/// animazione di "giro" (flip orizzontale) e poi segnala l'attivazione.
/// </summary>
public sealed class CoinButton : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private double _t;          // 0..1 progresso animazione
    private bool _animating;
    private bool _fired;
    private Image? _coin;

    /// <summary>Scattato al termine dell'animazione di clic.</summary>
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
            StartFlip();
        }
    }

    private void StartFlip()
    {
        if (_animating)
        {
            return;
        }

        _animating = true;
        _fired = false;
        _t = 0;
        _timer.Start();
    }

    private void Step()
    {
        _t += 0.04;

        // a metà giro (moneta di taglio) segnala l'attivazione, così il menu
        // compare mentre la moneta finisce di girare
        if (!_fired && _t >= 0.5)
        {
            _fired = true;
            Activated?.Invoke();
        }

        if (_t >= 1)
        {
            _t = 1;
            _timer.Stop();
            _animating = false;
        }

        Invalidate();
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

        // flip: la larghezza si stringe come una moneta che gira (cos 0->2π)
        var scale = _animating ? Math.Abs(Math.Cos(_t * Math.PI * 2)) : 1.0;
        var w = (float)(Width * scale);
        if (w < 1f)
        {
            w = 1f;
        }

        var x = (Width - w) / 2f;
        g.DrawImage(_coin, x, 0, w, Height);
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
