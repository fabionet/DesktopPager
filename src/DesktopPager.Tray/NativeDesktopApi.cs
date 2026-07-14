using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopPager.Tray;

/// <summary>
/// Stato della barra di scorrimento della ListView del desktop.
/// PageSize corrisponde alla larghezza (o altezza) visibile; MaxRange
/// all'estensione totale del contenuto.
/// </summary>
public readonly record struct DesktopScrollStatus(int Position, int MaxRange, int PageSize);

public static class NativeDesktopApi
{
    private const int LvmFirst = 0x1000;
    private const int LvmGetItemCount = LvmFirst + 4;
    private const int LvmScroll = LvmFirst + 20;
    private const int LvmGetItemSpacing = LvmFirst + 51;

    // Il desktop ha lo stile LVS_NOSCROLL: finche' e' attivo LVM_SCROLL viene
    // ignorato. Va rimosso prima di scorrere e ripristinato quando si torna
    // alla prima pagina, cosi' il desktop resta esattamente com'era.
    private const int LvsNoScroll = 0x2000;
    private const int LvsAlignLeft = 0x0800;
    private const int LvsAlignMask = 0x0C00;
    private const int GwlStyle = -16;
    private const int SbHorz = 0;
    private const int SbVert = 1;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint SendTimeoutMs = 500;

    public static int GetDesktopIconCount()
    {
        if (!TryGetDesktopListViewHandle(out var listView))
        {
            return 0;
        }

        return SendMessage(listView, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero).ToInt32();
    }

    public static bool TryGetDesktopClientRectangle(out Rectangle rectangle)
    {
        rectangle = Rectangle.Empty;
        if (!TryGetDesktopListViewHandle(out var listView))
        {
            return false;
        }

        if (!GetClientRect(listView, out var rect))
        {
            return false;
        }

        rectangle = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }

    /// <summary>
    /// Fa scorrere il desktop di <paramref name="pageDelta"/> pagine
    /// (positivo = avanti, negativo = indietro). Non sposta mai le icone:
    /// scorre soltanto la vista, quindi il layout resta intatto.
    /// </summary>
    public static bool TryScrollDesktopByPages(int pageDelta)
    {
        if (pageDelta == 0)
        {
            return true;
        }

        if (!TryGetDesktopListViewHandle(out var listView))
        {
            return false;
        }

        SetNoScrollStyle(listView, enabled: false);

        if (!GetClientRect(listView, out var rect))
        {
            return false;
        }

        var spacing = SendMessage(listView, LvmGetItemSpacing, IntPtr.Zero, IntPtr.Zero).ToInt64();
        var spacingX = (int)(spacing & 0xFFFF);
        var spacingY = (int)((spacing >> 16) & 0xFFFF);

        var horizontal = IsHorizontalLayout(listView);
        var dx = 0;
        var dy = 0;
        if (horizontal)
        {
            // una colonna di sovrapposizione per non perdere il filo
            var step = rect.Right - spacingX;
            if (step < spacingX)
            {
                step = rect.Right;
            }

            dx = pageDelta * step;
        }
        else
        {
            var step = rect.Bottom - spacingY;
            if (step < spacingY)
            {
                step = rect.Bottom;
            }

            dy = pageDelta * step;
        }

        var beforePosition = TryGetDesktopScrollStatus(out var before) ? before.Position : 0;

        var ok = SendListMessageTimeout(listView, LvmScroll, (IntPtr)dx, (IntPtr)dy, out var result)
            && result != IntPtr.Zero;

        // Quando la scrollbar compare per la prima volta la ListView fa un
        // re-layout che puo' assorbire parte dello scroll: verifica la
        // posizione raggiunta e correggi la differenza.
        if (ok && TryGetDesktopScrollStatus(out var after))
        {
            var maxPosition = Math.Max(0, after.MaxRange - after.PageSize + 1);
            var target = Math.Clamp(beforePosition + (horizontal ? dx : dy), 0, maxPosition);
            var diff = target - after.Position;
            if (diff != 0)
            {
                SendListMessageTimeout(
                    listView,
                    LvmScroll,
                    (IntPtr)(horizontal ? diff : 0),
                    (IntPtr)(horizontal ? 0 : diff),
                    out _);
            }
        }

        return ok;
    }

    /// <summary>
    /// Riporta il desktop alla prima pagina e ripristina lo stile originale
    /// (LVS_NOSCROLL), lasciando il desktop come l'abbiamo trovato.
    /// </summary>
    public static bool TryResetDesktopScroll()
    {
        if (!TryGetDesktopListViewHandle(out var listView))
        {
            return false;
        }

        SetNoScrollStyle(listView, enabled: false);
        SendListMessageTimeout(listView, LvmScroll, (IntPtr)(-32768), (IntPtr)(-32768), out _);
        SetNoScrollStyle(listView, enabled: true);
        InvalidateRect(listView, IntPtr.Zero, true);
        return true;
    }

    /// <summary>
    /// Legge posizione e range di scorrimento. Disponibile solo mentre si
    /// sta sfogliando (con LVS_NOSCROLL attivo la scrollbar non esiste).
    /// </summary>
    public static bool TryGetDesktopScrollStatus(out DesktopScrollStatus status)
    {
        status = default;
        if (!TryGetDesktopListViewHandle(out var listView))
        {
            return false;
        }

        var si = ScrollInfo.Create();
        var bar = IsHorizontalLayout(listView) ? SbHorz : SbVert;
        if (!GetScrollInfo(listView, bar, ref si))
        {
            return false;
        }

        if (si.nPage == 0 && si.nMax == 0)
        {
            return false;
        }

        status = new DesktopScrollStatus(si.nPos, si.nMax, (int)si.nPage);
        return true;
    }

    public static bool RegisterHotKey(IntPtr handle, int id, int modifiers, int key)
    {
        return RegisterHotKeyNative(handle, id, modifiers, key);
    }

    public static bool UnregisterHotKey(IntPtr handle, int id)
    {
        return UnregisterHotKeyNative(handle, id);
    }

    private static bool IsHorizontalLayout(IntPtr listView)
    {
        // Con "disposizione automatica" il desktop riempie colonne da sinistra
        // (LVS_ALIGNLEFT): le icone in eccesso finiscono oltre il bordo destro,
        // quindi si sfoglia in orizzontale. Altrimenti in verticale.
        var style = GetWindowStyle(listView);
        return (style & LvsAlignMask) == LvsAlignLeft;
    }

    private static void SetNoScrollStyle(IntPtr listView, bool enabled)
    {
        var style = GetWindowStyle(listView);
        var hasNoScroll = (style & LvsNoScroll) != 0;
        if (enabled == hasNoScroll)
        {
            return;
        }

        var newStyle = enabled ? style | LvsNoScroll : style & ~(long)LvsNoScroll;
        SetWindowStyle(listView, newStyle);
    }

    private static long GetWindowStyle(IntPtr hwnd)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, GwlStyle).ToInt64()
            : GetWindowLong32(hwnd, GwlStyle);
    }

    private static void SetWindowStyle(IntPtr hwnd, long style)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hwnd, GwlStyle, (IntPtr)style);
        }
        else
        {
            SetWindowLong32(hwnd, GwlStyle, (int)style);
        }
    }

    private static bool SendListMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        var sent = SendMessageTimeout(hwnd, msg, wParam, lParam, SmtoAbortIfHung, SendTimeoutMs, out result);
        return sent != IntPtr.Zero;
    }

    private static bool TryGetDesktopListViewHandle(out IntPtr listView)
    {
        listView = IntPtr.Zero;

        var progman = FindWindow("Progman", "Program Manager");
        var shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (shellView == IntPtr.Zero)
        {
            var worker = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null);
            while (worker != IntPtr.Zero && shellView == IntPtr.Zero)
            {
                shellView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
            }
        }

        if (shellView == IntPtr.Zero)
        {
            return false;
        }

        listView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");
        return listView != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;

        public static ScrollInfo Create()
        {
            return new ScrollInfo
            {
                cbSize = (uint)Marshal.SizeOf<ScrollInfo>(),
                fMask = 0x17 // SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS
            };
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetScrollInfo(IntPtr hwnd, int fnBar, ref ScrollInfo lpsi);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    private static extern bool RegisterHotKeyNative(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    private static extern bool UnregisterHotKeyNative(IntPtr hWnd, int id);
}
