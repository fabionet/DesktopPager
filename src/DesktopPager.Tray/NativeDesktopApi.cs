using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopPager.Tray;

public static class NativeDesktopApi
{
    private const int LvmFirst = 0x1000;
    private const int LvmGetItemCount = LvmFirst + 4;
    private const int LvmSetItemPosition32 = LvmFirst + 49;

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


    public static bool TrySetDesktopIconPosition(int iconIndex, Point position)
    {
        if (!TryGetDesktopListViewHandle(out var listView))
        {
            return false;
        }

        var lp = MakeLParam(position.X, position.Y);
        return SendMessage(listView, LvmSetItemPosition32, (IntPtr)iconIndex, lp) != IntPtr.Zero;
    }

    public static bool RegisterHotKey(IntPtr handle, int id, int modifiers, int key)
    {
        return RegisterHotKeyNative(handle, id, modifiers, key);
    }

    public static bool UnregisterHotKey(IntPtr handle, int id)
    {
        return UnregisterHotKeyNative(handle, id);
    }

    private static IntPtr MakeLParam(int low, int high)
    {
        return (IntPtr)((high << 16) | (low & 0xFFFF));
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);


    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    private static extern bool RegisterHotKeyNative(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    private static extern bool UnregisterHotKeyNative(IntPtr hWnd, int id);
}
