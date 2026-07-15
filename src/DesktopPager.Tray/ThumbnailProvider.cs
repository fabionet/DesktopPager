using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopPager.Tray;

/// <summary>
/// Anteprime dei file tramite la shell (IShellItemImageFactory): stessa
/// miniatura che mostra Explorer, con fallback all'icona associata.
/// </summary>
public static class ThumbnailProvider
{
    /// <summary>Flag SIIGBF di IShellItemImageFactory.GetImage.</summary>
    [Flags]
    public enum ThumbFlags
    {
        /// <summary>Anteprima vera: se manca, la shell la GENERA (lento).</summary>
        ResizeToFit = 0x0000,
        BiggerSizeOk = 0x0001,
        /// <summary>Solo da memoria: fallisce invece di generarla (veloce).</summary>
        MemoryOnly = 0x0002,
        /// <summary>Solo l'icona, mai l'anteprima (veloce).</summary>
        IconOnly = 0x0004,
        ThumbnailOnly = 0x0008,
        /// <summary>Solo se già in cache: fallisce invece di generarla (veloce).</summary>
        InCacheOnly = 0x0010,
        ScaleUp = 0x0100
    }

    public static Bitmap? GetThumbnail(string path, int size) =>
        GetThumbnail(path, size, ThumbFlags.ResizeToFit);

    public static Bitmap? GetThumbnail(string path, int size, ThumbFlags flags)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (hr != 0 || factory is null)
            {
                return Fallback(path, size);
            }

            var s = new SIZE { cx = size, cy = size };
            factory.GetImage(s, (int)flags, out var hbmp);
            Marshal.ReleaseComObject(factory);
            if (hbmp == IntPtr.Zero)
            {
                return Fallback(path, size);
            }

            try
            {
                using var raw = Image.FromHbitmap(hbmp);
                return new Bitmap(raw);
            }
            finally
            {
                DeleteObject(hbmp);
            }
        }
        catch
        {
            return Fallback(path, size);
        }
    }

    private static Bitmap? Fallback(string path, int size)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            // ToBitmap()+DrawImage conserva l'alfa: DrawIcon su superficie GDI+
            // 32bpp lascerebbe un quadratino nero attorno alle icone legacy
            using var src = icon.ToBitmap();
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, new Rectangle(size / 4, size / 4, size / 2, size / 2));
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
