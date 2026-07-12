using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPager.Tray;

public static class NativeDesktopApi
{
    private const int LvmFirst = 0x1000;
    private const int LvmGetItemCount = LvmFirst + 4;
    private const int LvmGetItemPosition = LvmFirst + 16;
    private const int LvmSetItemPosition32 = LvmFirst + 49;

    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

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

    public static bool TryGetDesktopIconPositions(out Dictionary<int, Point> positions)
    {
        positions = new Dictionary<int, Point>();
        if (!TryGetDesktopListViewHandle(out var listView))
        {
            return false;
        }

        var count = GetDesktopIconCount();
        if (count <= 0)
        {
            return true;
        }

        GetWindowThreadProcessId(listView, out var processId);
        var processHandle = OpenProcess(
            ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation,
            false,
            processId);
        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        var pointSize = Marshal.SizeOf<NativePoint>();
        var remoteBuffer = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)pointSize, MemCommit | MemReserve, PageReadWrite);
        if (remoteBuffer == IntPtr.Zero)
        {
            CloseHandle(processHandle);
            return false;
        }

        var ok = true;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var sent = SendMessage(listView, LvmGetItemPosition, (IntPtr)i, remoteBuffer);
                if (sent == IntPtr.Zero)
                {
                    ok = false;
                    break;
                }

                var localBuffer = new byte[pointSize];
                if (!ReadProcessMemory(processHandle, remoteBuffer, localBuffer, localBuffer.Length, out _))
                {
                    ok = false;
                    break;
                }

                var point = MemoryMarshal.Read<NativePoint>(localBuffer);
                positions[i] = new Point(point.X, point.Y);
            }
        }
        finally
        {
            VirtualFreeEx(processHandle, remoteBuffer, 0, MemRelease);
            CloseHandle(processHandle);
        }

        return ok;
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
    private struct NativePoint
    {
        public int X;
        public int Y;
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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, uint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, [Out] byte[] buffer, int size, out IntPtr bytesRead);

    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    private static extern bool RegisterHotKeyNative(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    private static extern bool UnregisterHotKeyNative(IntPtr hWnd, int id);
}
