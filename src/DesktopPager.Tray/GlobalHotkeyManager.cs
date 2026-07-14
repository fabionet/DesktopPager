using System.Windows.Forms;

namespace DesktopPager.Tray;

public sealed class GlobalHotkeyManager : IDisposable
{
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;

    private const int HotkeyNextPage = 1;
    private const int HotkeyPreviousPage = 2;
    private const int HotkeyMainPage = 3;
    private const int HotkeyRestartExplorer = 4;

    private IntPtr _windowHandle;

    public bool Register(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        var modifiers = ModControl | ModAlt | ModNoRepeat;

        var nextOk = NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyNextPage, modifiers, (int)Keys.PageDown);
        var previousOk = NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyPreviousPage, modifiers, (int)Keys.PageUp);
        var mainOk = NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyMainPage, modifiers, (int)Keys.Home);
        var restartOk = NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyRestartExplorer, modifiers, (int)Keys.End);

        return nextOk && previousOk && mainOk && restartOk;
    }

    public bool HandleMessage(Message m, DesktopPageManager pageManager, Action restartExplorer)
    {
        if (m.Msg != WmHotkey)
        {
            return false;
        }

        switch (m.WParam.ToInt32())
        {
            case HotkeyNextPage:
                return pageManager.NextPage();
            case HotkeyPreviousPage:
                return pageManager.PreviousPage();
            case HotkeyMainPage:
                return pageManager.GoToMainPage();
            case HotkeyRestartExplorer:
                restartExplorer();
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeDesktopApi.UnregisterHotKey(_windowHandle, HotkeyNextPage);
        NativeDesktopApi.UnregisterHotKey(_windowHandle, HotkeyPreviousPage);
        NativeDesktopApi.UnregisterHotKey(_windowHandle, HotkeyMainPage);
        NativeDesktopApi.UnregisterHotKey(_windowHandle, HotkeyRestartExplorer);
    }
}
