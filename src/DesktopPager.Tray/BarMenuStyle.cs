using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DesktopPager.Tray;

/// <summary>
/// Stile condiviso dei menu: stessa palette rosso scuro in rilievo della barra
/// (gradiente chiaro->scuro, bordo in luce) e angoli smussati. Si applica al
/// menu e, ricorsivamente, ai suoi sottomenu.
/// </summary>
public static class BarMenuStyle
{
    private const int Radius = 10;

    /// <summary>Renderer predefinito per qualunque ToolStrip dell'app (rete di sicurezza).</summary>
    public static void ApplyGlobal()
    {
        ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;
        ToolStripManager.Renderer = new BarRenderer();
    }

    /// <summary>Menu contestuale già nello stile della barra.</summary>
    public static ContextMenuStrip New()
    {
        var menu = new ContextMenuStrip();
        Apply(menu);
        return menu;
    }

    /// <summary>
    /// Si può chiamare anche subito dopo la creazione: i sottomenu vengono
    /// stilati all'apertura, quando le voci ci sono di sicuro.
    /// </summary>
    public static void Apply(ToolStripDropDown menu)
    {
        menu.Renderer = new BarRenderer();
        menu.BackColor = BarStyle.Mid;
        menu.ForeColor = BarStyle.Text;
        RoundCorners(menu);
        menu.Opened += (_, _) => StyleItems(menu);
    }

    private static void StyleItems(ToolStripDropDown menu)
    {
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = BarStyle.Text;
            // il controllo sul renderer evita di riagganciare a ogni apertura
            if (item is ToolStripMenuItem mi && mi.HasDropDownItems && mi.DropDown.Renderer is not BarRenderer)
            {
                Apply(mi.DropDown);
            }
        }
    }

    private static void RoundCorners(ToolStripDropDown menu)
    {
        void Apply()
        {
            if (menu.Width <= 2 || menu.Height <= 2)
            {
                return;
            }

            using var path = Rounded(new Rectangle(0, 0, menu.Width, menu.Height), Radius);
            menu.Region = new Region(path);
        }

        menu.Opened += (_, _) => Apply();
        menu.SizeChanged += (_, _) => Apply();
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (r.Width <= d || r.Height <= d)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class BarRenderer : ToolStripProfessionalRenderer
    {
        public BarRenderer() : base(new BarColors()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var r = new Rectangle(Point.Empty, e.ToolStrip.Size);
            if (r.Width <= 0 || r.Height <= 0)
            {
                base.OnRenderToolStripBackground(e);
                return;
            }

            using var b = new LinearGradientBrush(r, BarStyle.Top, BarStyle.Bottom, 90f);
            e.Graphics.FillRectangle(b, r);
        }

        // niente colonna delle icone in tinta piatta: lascia passare il gradiente
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            if (r.Width <= 0 || r.Height <= 0)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(r, Radius);
            using var pen = new Pen(BarStyle.Highlight);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? BarStyle.Text : Color.FromArgb(160, 150, 150);
            base.OnRenderItemText(e);
        }
    }

    private sealed class BarColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => BarStyle.Mid;
        public override Color MenuItemSelected => BarStyle.Hover;
        public override Color MenuItemSelectedGradientBegin => BarStyle.Hover;
        public override Color MenuItemSelectedGradientEnd => BarStyle.Hover;
        public override Color MenuItemBorder => BarStyle.Highlight;
        public override Color MenuBorder => BarStyle.Shadow;
        public override Color MenuItemPressedGradientBegin => BarStyle.Mid;
        public override Color MenuItemPressedGradientMiddle => BarStyle.Mid;
        public override Color MenuItemPressedGradientEnd => BarStyle.Dark;
        public override Color ImageMarginGradientBegin => BarStyle.Dark;
        public override Color ImageMarginGradientMiddle => BarStyle.Dark;
        public override Color ImageMarginGradientEnd => BarStyle.Dark;
        public override Color SeparatorDark => BarStyle.Shadow;
        public override Color SeparatorLight => BarStyle.Highlight;
        public override Color CheckBackground => BarStyle.Highlight;
        public override Color CheckSelectedBackground => BarStyle.Highlight;
        public override Color CheckPressedBackground => BarStyle.Highlight;
    }
}
