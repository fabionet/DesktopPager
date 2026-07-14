using System;

namespace DesktopPager.Tray.DesktopEffects;

/// <summary>
/// Hook globale di basso livello del mouse. I callback arrivano sul thread che
/// installa l'hook (il thread UI, che ha un message loop), quindi è sicuro
/// toccare le finestre WPF/WinForms dai gestori. Restituendo true dal gestore
/// l'evento viene "mangiato" (non arriva alle altre applicazioni).
/// </summary>
internal sealed class GlobalMouseHook : IDisposable
{
    /// <summary>msg (WM_*), x, y schermo. Restituire true per bloccare l'evento.</summary>
    public Func<int, int, int, bool>? OnMouse;

    private EffectsNative.LowLevelMouseProc? _proc;
    private IntPtr _hook;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _proc = HookProc; // mantieni un riferimento vivo: evita la GC del delegate
        var hMod = EffectsNative.GetModuleHandle(null);
        _hook = EffectsNative.SetWindowsHookEx(EffectsNative.WH_MOUSE_LL, _proc, hMod, 0);
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            EffectsNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && OnMouse is not null)
        {
            var data = System.Runtime.InteropServices.Marshal
                .PtrToStructure<EffectsNative.MSLLHOOKSTRUCT>(lParam);
            try
            {
                if (OnMouse(wParam.ToInt32(), data.pt.x, data.pt.y))
                {
                    return (IntPtr)1; // consuma l'evento
                }
            }
            catch
            {
                // un gestore che lancia non deve bloccare l'input di sistema
            }
        }

        return EffectsNative.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
