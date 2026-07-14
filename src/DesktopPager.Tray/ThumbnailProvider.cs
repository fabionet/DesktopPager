using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopPager.Tray;

/// <summary>
/// Anteprime dei file tramite la shell (IShellItemImageFactory): stessa
/// miniatura che mostra Explorer, con fallback all'icona associata.
/// </summary>
public static class ThumbnailProvider
{
    public static Bitmap? GetThumbnail(string path, int size)
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
            // SIIGBF_RESIZETOFIT = 0
            factory.GetImage(s, 0, out var hbmp);
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

            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.DrawIcon(icon, new Rectangle(size / 4, size / 4, size / 2, size / 2));
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
