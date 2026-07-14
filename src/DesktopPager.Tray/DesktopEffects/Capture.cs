using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DesktopPager.Tray.DesktopEffects;

/// <summary>
/// Cattura di finestre e schermo in <see cref="BitmapSource"/> (freezato, per
/// poterlo usare da qualsiasi thread e come texture WPF).
/// </summary>
internal static class Capture
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>Istantanea di una finestra tramite PrintWindow. Null se non riesce.</summary>
    public static BitmapSource? Window(IntPtr hwnd, out EffectsNative.RECT rect)
    {
        rect = default;
        if (!EffectsNative.IsWindow(hwnd) || !EffectsNative.GetWindowRect(hwnd, out rect))
        {
            return null;
        }

        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0 || w > 20000 || h > 20000)
        {
            return null;
        }

        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try
            {
                if (!EffectsNative.PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                {
                    return null;
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        return ToBitmapSource(bmp);
    }

    /// <summary>Istantanea di una regione dello schermo (px schermo).</summary>
    public static BitmapSource Screen(int x, int y, int w, int h)
    {
        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
        }

        return ToBitmapSource(bmp);
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bmp)
    {
        var hBitmap = bmp.GetHbitmap();
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            EffectsNative.DeleteObject(hBitmap); // niente leak di GDI
        }
    }
}
