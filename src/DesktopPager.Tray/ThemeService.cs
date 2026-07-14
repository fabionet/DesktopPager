using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopPager.Tray;

/// <summary>
/// Colori coerenti col tema di Windows (chiaro/scuro + colore accento DWM).
/// </summary>
public static class ThemeService
{
    public static bool IsDarkTheme()
    {
        try
        {
            // la taskbar segue SystemUsesLightTheme
            var v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 1);
            return v is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }

    public static Color BarBackground => IsDarkTheme()
        ? Color.FromArgb(255, 32, 32, 36)
        : Color.FromArgb(255, 238, 238, 242);

    public static Color BarForeground => IsDarkTheme() ? Color.White : Color.FromArgb(32, 32, 32);

    public static Color ButtonHover => IsDarkTheme()
        ? Color.FromArgb(60, 60, 66)
        : Color.FromArgb(215, 215, 222);

    public static Color Accent
    {
        get
        {
            try
            {
                DwmGetColorizationColor(out var argb, out _);
                var c = Color.FromArgb(unchecked((int)argb));
                return Color.FromArgb(255, c.R, c.G, c.B);
            }
            catch
            {
                return Color.FromArgb(0, 120, 215);
            }
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorization, out bool opaqueBlend);
}
