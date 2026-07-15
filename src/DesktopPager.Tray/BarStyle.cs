using System.Drawing;
using System.Globalization;
using System.IO;

namespace DesktopPager.Tray;

/// <summary>
/// Palette in rilievo condivisa da barra, menu e menu Start. Tutte le tinte
/// derivano da un unico colore <see cref="Base"/>, scelto dall'utente dal menu
/// della tray e ricordato tra un avvio e l'altro. I consumatori devono leggere
/// queste proprietà al momento del disegno (non copiarle in campi statici), così
/// il cambio colore si vede subito.
/// </summary>
public static class BarStyle
{
    /// <summary>Scattato quando cambia il colore: chi disegna deve ridisegnarsi.</summary>
    public static event Action? Changed;

    public static readonly Color DefaultBase = Color.FromArgb(98, 18, 18); // rosso scuro

    private static Color _base = DefaultBase;

    /// <summary>Colore base della palette (corrisponde a <see cref="Mid"/>).</summary>
    public static Color Base
    {
        get => _base;
        set
        {
            if (_base == value)
            {
                return;
            }

            _base = value;
            Save();
            Changed?.Invoke();
        }
    }

    public static Color Top => Shade(1.27, 0);       // gradiente: alto (chiaro)
    public static Color Bottom => Shade(0.59, 0);    // gradiente: basso (scuro)
    public static Color Mid => _base;                // sfondo pulsanti/pannelli
    public static Color Dark => Shade(0.73, 0);      // pannelli più scuri
    public static Color Hover => Shade(1.40, 16);    // hover
    public static Color Highlight => Shade(1.45, 55);// bordo in luce
    public static Color Shadow => Shade(0.22, 0);    // ombra
    public static Color Text => Color.White;

    /// <summary>Tinte pronte per il menu della tray.</summary>
    public static (string Name, Color Color)[] Presets { get; } =
    {
        ("Rosso scuro", DefaultBase),
        ("Blu notte", Color.FromArgb(22, 52, 108)),
        ("Verde bosco", Color.FromArgb(22, 76, 44)),
        ("Viola", Color.FromArgb(64, 30, 104)),
        ("Antracite", Color.FromArgb(54, 56, 64)),
        ("Arancio scuro", Color.FromArgb(122, 58, 12)),
        ("Petrolio", Color.FromArgb(16, 74, 82))
    };

    // scala i canali (mantiene tinta e saturazione) e aggiunge un rialzo per le
    // tinte chiare, così il rilievo resta leggibile con qualunque colore base
    private static Color Shade(double factor, int lift) => Color.FromArgb(
        Channel(_base.R * factor + lift),
        Channel(_base.G * factor + lift),
        Channel(_base.B * factor + lift));

    private static int Channel(double v) => v < 0 ? 0 : v > 255 ? 255 : (int)v;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopPager3D-OS", "theme.cfg");

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return;
            }

            var text = File.ReadAllText(ConfigPath).Trim();
            if (int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                _base = Color.FromArgb(255, Color.FromArgb(rgb));
            }
        }
        catch
        {
            // configurazione illeggibile: resta il rosso predefinito
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, (_base.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));
        }
        catch
        {
            // impossibile salvare: non è critico
        }
    }
}
