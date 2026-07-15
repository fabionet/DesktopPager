namespace DesktopPager.Tray;

/// <summary>
/// Palette del tesseratto (ipercubo): emblema originale di DesktopPager3D-OS,
/// usato sia sulla moneta della barra (disegnata con GDI+) sia sulla porta
/// dell'intro 3D (WPF). I valori RGB stanno qui una volta sola così i due
/// loghi non divergono; ogni consumatore li converte nel proprio tipo Color.
/// </summary>
public static class TesseractPalette
{
    /// <summary>Faccia superiore del tesseratto.</summary>
    public static readonly (byte R, byte G, byte B) Top = (0xFF, 0x4D, 0x4D);    // rosso
    /// <summary>Faccia destra.</summary>
    public static readonly (byte R, byte G, byte B) Right = (0xFF, 0xC3, 0x00);  // ambra
    /// <summary>Faccia inferiore.</summary>
    public static readonly (byte R, byte G, byte B) Bottom = (0x2E, 0xCC, 0x71); // verde
    /// <summary>Faccia sinistra.</summary>
    public static readonly (byte R, byte G, byte B) Left = (0x3D, 0xA9, 0xFC);   // blu
    /// <summary>Cubo interno.</summary>
    public static readonly (byte R, byte G, byte B) Core = (0x7B, 0x5C, 0xFF);   // violetto
    /// <summary>Spigoli, sulla moneta (fondo chiaro).</summary>
    public static readonly (byte R, byte G, byte B) Edge = (0x10, 0x18, 0x28);   // quasi nero
}
