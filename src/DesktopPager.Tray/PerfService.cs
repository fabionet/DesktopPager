using System.Runtime.InteropServices;

namespace DesktopPager.Tray;

/// <summary>
/// Lettore di prestazioni leggero: uso CPU (dai tempi di sistema) e uso RAM.
/// </summary>
public sealed class PerfService
{
    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _hasPrev;

    public int CpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleT = ToUlong(idle);
        var kernelT = ToUlong(kernel);
        var userT = ToUlong(user);

        var result = 0;
        if (_hasPrev)
        {
            var idleDiff = idleT - _prevIdle;
            var kernelDiff = kernelT - _prevKernel;
            var userDiff = userT - _prevUser;
            var total = kernelDiff + userDiff; // kernel include già l'idle
            if (total > 0)
            {
                result = (int)Math.Clamp(100.0 * (total - idleDiff) / total, 0, 100);
            }
        }

        _prevIdle = idleT;
        _prevKernel = kernelT;
        _prevUser = userT;
        _hasPrev = true;
        return result;
    }

    public int RamPercent()
    {
        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref ms) ? (int)ms.dwMemoryLoad : 0;
    }

    private static ulong ToUlong(FILETIME ft) => ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
