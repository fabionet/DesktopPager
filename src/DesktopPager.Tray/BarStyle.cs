using System.Drawing;

namespace DesktopPager.Tray;

/// <summary>
/// Palette rosso scuro in rilievo condivisa dalla barra e dal menu Start.
/// </summary>
public static class BarStyle
{
    public static readonly Color Top = Color.FromArgb(124, 26, 26);      // gradiente: alto (chiaro)
    public static readonly Color Bottom = Color.FromArgb(58, 8, 8);      // gradiente: basso (scuro)
    public static readonly Color Mid = Color.FromArgb(98, 18, 18);       // sfondo pulsanti/pannelli
    public static readonly Color Dark = Color.FromArgb(72, 12, 12);      // pannelli più scuri
    public static readonly Color Hover = Color.FromArgb(152, 42, 42);    // hover
    public static readonly Color Highlight = Color.FromArgb(188, 78, 78);// bordo in luce
    public static readonly Color Shadow = Color.FromArgb(22, 2, 2);      // ombra
    public static readonly Color Text = Color.White;
}
