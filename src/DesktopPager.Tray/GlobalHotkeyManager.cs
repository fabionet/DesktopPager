using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopPager.Tray;

public sealed class GlobalHotkeyManager : IDisposable
{
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;

    private const int HotkeyNextPage = 1;
    private const int HotkeyPreviousPage = 2;
    private const int HotkeyMainPage = 3;
    private const int HotkeyRestartExplorer = 4;
    private const int HotkeyFlipScreen = 5;        // Ctrl+Alt+Su: sottosopra
    private const int HotkeyResetScreen = 6;       // Ctrl+Alt+Giu: normale
    private const int HotkeyRotateLeft = 7;        // Ctrl+Alt+Sinistra
    private const int HotkeyRotateRight = 8;       // Ctrl+Alt+Destra
    private const int HotkeyPanicReset = 9;        // Ctrl+Alt+Shift+^: emergenza

    private IntPtr _windowHandle;

    public bool Register(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        var modifiers = ModControl | ModAlt | ModNoRepeat;

        var ok = NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyNextPage, modifiers, (int)Keys.PageDown);
        ok &= NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyPreviousPage, modifiers, (int)Keys.PageUp);
        ok &= NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyMainPage, modifiers, (int)Keys.Home);
        ok &= NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyRestartExplorer, modifiers, (int)Keys.End);
        ok &= NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyFlipScreen, modifiers, (int)Keys.Up);
        ok &= NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyResetScreen, modifiers, (int)Keys.Down);
        ok &= NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyRotateLeft, modifiers, (int)Keys.Left);
        ok &= NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyRotateRight, modifiers, (int)Keys.Right);
        ok &= NativeDesktopApi.RegisterHotKey(_windowHandle, HotkeyPanicReset, modifiers | ModShift, GetCaretVirtualKey());

        return ok;
    }

    public bool HandleMessage(Message m, DesktopPageManager pageManager, Action restartExplorer, Action<int> rotateScreen)
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
            case HotkeyFlipScreen:
                rotateScreen(ScreenRotationService.Orientation180);
                return false;
            case HotkeyResetScreen:
            case HotkeyPanicReset:
                rotateScreen(ScreenRotationService.OrientationDefault);
                return false;
            case HotkeyRotateLeft:
                rotateScreen(ScreenRotationService.Orientation90);
                return false;
            case HotkeyRotateRight:
                rotateScreen(ScreenRotationService.Orientation270);
                return false;
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

        for (var id = HotkeyNextPage; id <= HotkeyPanicReset; id++)
        {
            NativeDesktopApi.UnregisterHotKey(_windowHandle, id);
        }
    }

    // Tasto del carattere '^' nel layout di tastiera corrente
    // (sull'italiana e' Shift+ì, virtual key 0xDD).
    private static int GetCaretVirtualKey()
    {
        var scan = VkKeyScanW('^');
        return scan == -1 ? 0xDD : scan & 0xFF;
    }

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);
}
