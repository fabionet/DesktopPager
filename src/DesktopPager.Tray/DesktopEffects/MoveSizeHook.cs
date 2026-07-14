using System;

namespace DesktopPager.Tray.DesktopEffects;

/// <summary>
/// Hook WinEvent per l'inizio/fine dello spostamento di una finestra
/// (trascinamento della barra del titolo). È il "trigger" naturale, alla
/// Compiz, dell'effetto gelatina: nessun tasto speciale, non combatte il
/// ciclo di spostamento del sistema (la finestra reale continua a muoversi).
/// </summary>
internal sealed class MoveSizeHook : IDisposable
{
    public event Action<IntPtr>? MoveStart;
    public event Action<IntPtr>? MoveEnd;

    private EffectsNative.WinEventDelegate? _proc;
    private IntPtr _hook;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _proc = OnWinEvent; // riferimento vivo: evita la GC
        _hook = EffectsNative.SetWinEventHook(
            EffectsNative.EVENT_SYSTEM_MOVESIZESTART,
            EffectsNative.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _proc, 0, 0,
            EffectsNative.WINEVENT_OUTOFCONTEXT);
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            EffectsNative.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // interessa solo l'evento della finestra intera
        if (idObject != EffectsNative.OBJID_WINDOW || hwnd == IntPtr.Zero)
        {
            return;
        }

        if (eventType == EffectsNative.EVENT_SYSTEM_MOVESIZESTART)
        {
            MoveStart?.Invoke(hwnd);
        }
        else if (eventType == EffectsNative.EVENT_SYSTEM_MOVESIZEEND)
        {
            MoveEnd?.Invoke(hwnd);
        }
    }

    public void Dispose() => Uninstall();
}
