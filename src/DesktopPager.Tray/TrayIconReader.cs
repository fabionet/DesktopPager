using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPager.Tray;

/// <summary>
/// Legge le icone della notification area (system tray): le app in esecuzione
/// ridotte a icona. Enumera i pulsanti della ToolbarWindow32 della tray
/// (visibile e overflow) leggendo la memoria del processo di Explorer.
/// </summary>
public static class TrayIconReader
{
    public sealed record TrayEntry(string Tooltip, IntPtr OwnerHwnd, uint Id, uint CallbackMessage);

    private const uint TB_BUTTONCOUNT = 0x0418;
    private const uint TB_GETBUTTON = 0x0417;
    private const uint TB_GETBUTTONTEXTW = 0x044B;
    private const byte TBSTATE_HIDDEN = 0x08;

    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    public static List<TrayEntry> Read()
    {
        var result = new List<TrayEntry>();
        foreach (var toolbar in FindTrayToolbars())
        {
            ReadToolbar(toolbar, result);
        }

        // dedup per finestra proprietaria + tooltip
        return result
            .GroupBy(e => e.OwnerHwnd + "|" + e.Tooltip)
            .Select(g => g.First())
            .Where(e => !string.IsNullOrWhiteSpace(e.Tooltip))
            .OrderBy(e => e.Tooltip, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<IntPtr> FindTrayToolbars()
    {
        // tray visibile: Shell_TrayWnd > TrayNotifyWnd > SysPager > ToolbarWindow32
        var tray = FindWindow("Shell_TrayWnd", null);
        var notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        var pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
        var tb1 = FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null);
        if (tb1 != IntPtr.Zero)
        {
            yield return tb1;
        }

        // tray overflow (icone nascoste): NotifyIconOverflowWindow > ToolbarWindow32
        var overflow = FindWindow("NotifyIconOverflowWindow", null);
        var tb2 = FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null);
        if (tb2 != IntPtr.Zero)
        {
            yield return tb2;
        }
    }

    private static void ReadToolbar(IntPtr toolbar, List<TrayEntry> result)
    {
        GetWindowThreadProcessId(toolbar, out var pid);
        if (pid == 0)
        {
            return;
        }

        var hProc = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION, false, pid);
        if (hProc == IntPtr.Zero)
        {
            return;
        }

        var remote = VirtualAllocEx(hProc, IntPtr.Zero, 4096, MEM_COMMIT, PAGE_READWRITE);
        if (remote == IntPtr.Zero)
        {
            CloseHandle(hProc);
            return;
        }

        try
        {
            var count = (int)SendMessage(toolbar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
            var btn = new byte[32];       // TBBUTTON (x64)
            var tray = new byte[32];      // TRAYDATA (x64)
            var text = new byte[512];

            for (var i = 0; i < count; i++)
            {
                SendMessage(toolbar, TB_GETBUTTON, (IntPtr)i, remote);
                if (!ReadProcessMemory(hProc, remote, btn, btn.Length, out _))
                {
                    continue;
                }

                var idCommand = BitConverter.ToInt32(btn, 4);
                var fsState = btn[8];
                var dwData = (IntPtr)BitConverter.ToInt64(btn, 16);
                if ((fsState & TBSTATE_HIDDEN) != 0 || dwData == IntPtr.Zero)
                {
                    continue;
                }

                if (!ReadProcessMemory(hProc, dwData, tray, tray.Length, out _))
                {
                    continue;
                }

                var ownerHwnd = (IntPtr)BitConverter.ToInt64(tray, 0);
                var uId = BitConverter.ToUInt32(tray, 8);
                var uCallback = BitConverter.ToUInt32(tray, 12);

                // testo/tooltip del pulsante
                var len = (int)SendMessage(toolbar, TB_GETBUTTONTEXTW, (IntPtr)idCommand, remote);
                var tooltip = "";
                if (len > 0)
                {
                    var bytes = Math.Min((len + 1) * 2, text.Length);
                    if (ReadProcessMemory(hProc, remote, text, bytes, out _))
                    {
                        tooltip = Encoding.Unicode.GetString(text, 0, len * 2);
                    }
                }

                if (string.IsNullOrWhiteSpace(tooltip))
                {
                    continue;
                }

                // il tooltip può avere più righe: prendi la prima
                var nl = tooltip.IndexOfAny(new[] { '\r', '\n' });
                if (nl >= 0)
                {
                    tooltip = tooltip[..nl];
                }

                result.Add(new TrayEntry(tooltip.Trim(), ownerHwnd, uId, uCallback));
            }
        }
        catch
        {
            // lettura fallita: ignora questa toolbar
        }
        finally
        {
            VirtualFreeEx(hProc, remote, 0, MEM_RELEASE);
            CloseHandle(hProc);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll")]
    private static extern IntPtr VirtualAllocEx(IntPtr hProc, IntPtr addr, uint size, uint type, uint protect);

    [DllImport("kernel32.dll")]
    private static extern bool VirtualFreeEx(IntPtr hProc, IntPtr addr, uint size, uint type);

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr hProc, IntPtr addr, byte[] buffer, int size, out int read);
}
