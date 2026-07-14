using System.Drawing;
using System.Windows.Forms;

namespace DesktopPager.Tray;

/// <summary>
/// Pulsante icona piatto e realmente trasparente: mostra lo sfondo della
/// barra (il gradiente) senza la "tessera" quadrata dei normali Button;
/// evidenzia solo al passaggio del mouse. Disegna un'immagine oppure il
/// testo (glifo) centrato.
/// </summary>
public sealed class FlatIconButton : Control
{
    private bool _hover;
    private Image? _image;

    public FlatIconButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public Color HoverColor { get; set; } = Color.FromArgb(150, 40, 40);

    public Image? Image
    {
        get => _image;
        set { _image = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        if (_hover)
        {
            using var b = new SolidBrush(HoverColor);
            g.FillRectangle(b, ClientRectangle);
        }

        if (_image is not null)
        {
            var x = (Width - _image.Width) / 2;
            var y = (Height - _image.Height) / 2;
            g.DrawImage(_image, x, y, _image.Width, _image.Height);
        }
        else if (!string.IsNullOrEmpty(Text))
        {
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, Color.Transparent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
