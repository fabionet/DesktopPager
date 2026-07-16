using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace DesktopPager.Tray.DesktopEffects;

/// <summary>Come si sfoglia l'elenco applicazioni della barra di Windows.</summary>
public enum TaskbarScrollMode
{
    /// <summary>Metodo di Windows: solo le freccette della barra di scorrimento.</summary>
    Off = 0,
    /// <summary>Rotellina su/giu'.</summary>
    Wheel = 1,
    /// <summary>Rotellina inclinabile destra/sinistra.</summary>
    TiltWheel = 2
}

/// <summary>
/// Ritocchi alla barra delle applicazioni di Windows, da fuori processo e senza
/// iniezione di codice in Explorer: si usano solo messaggi e chiamate che
/// Windows consente verso finestre altrui, così un errore non può far cadere la
/// shell.
///
/// Come funziona lo scorrimento: quando l'elenco applicazioni non ci sta nella
/// barra, Explorer mette una <b>vera barra di scorrimento standard</b>
/// (WS_VSCROLL) su MSTaskSwWClass — quelle sono le freccette bianche. Sfogliare
/// una pagina significa mandarle lo stesso WM_VSCROLL/SB_LINEUP che manderebbe
/// il clic sulla freccetta, quindi non stiamo forzando nulla.
///
/// Le freccette si nascondono con ShowScrollBar: lo scorrimento continua a
/// funzionare (posizione e intervallo restano), sparisce solo il disegno.
/// Explorer le rimette quando rifà l'impaginazione, perciò <see cref="Sync"/>
/// va richiamata periodicamente.
/// </summary>
internal sealed class TaskbarTweaks : IDisposable
{
    private const string TrayClass = "Shell_TrayWnd";
    private const string SecondaryTrayClass = "Shell_SecondaryTrayWnd";
    private const string TaskSwClass = "MSTaskSwWClass";

    private TaskbarScrollMode _mode = TaskbarScrollMode.Off;
    private bool _tint;

    /// <summary>Finestre a cui abbiamo tolto le freccette: da rimettere a posto.</summary>
    private readonly HashSet<IntPtr> _hidden = new();

    /// <summary>Ultima barra vista: se cambia, Explorer e' ripartito.</summary>
    private IntPtr _lastTray;

    public TaskbarScrollMode ScrollMode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }
            _mode = value;
            if (value == TaskbarScrollMode.Off)
            {
                RestoreScrollBars();
            }
            Sync();
        }
    }

    public bool TintEnabled
    {
        get => _tint;
        set
        {
            if (_tint == value)
            {
                return;
            }
            _tint = value;
            if (value)
            {
                ApplyTint();
            }
            else
            {
                RestoreTint();
            }
        }
    }

    /// <summary>Riapplica il colore (il colore della barra e' cambiato).</summary>
    public void RefreshTint()
    {
        if (_tint)
        {
            ApplyTint();
        }
    }

    /// <summary>
    /// Rimette le cose come le vogliamo: da chiamare a bassa frequenza, perche'
    /// Explorer rifa' l'impaginazione (e rimette le freccette) quando apri o
    /// chiudi finestre, e riparte da zero se lo riavvii.
    /// </summary>
    public void Sync()
    {
        try
        {
            var tray = EffectsNative.FindWindow(TrayClass, null);
            var restarted = tray != _lastTray;
            _lastTray = tray;
            if (restarted)
            {
                _hidden.Clear(); // gli handle vecchi non esistono piu'
            }

            if (_mode != TaskbarScrollMode.Off)
            {
                foreach (var sw in TaskSwitchers())
                {
                    HideScrollBar(sw);
                }
            }

            if (_tint && restarted)
            {
                ApplyTint();
            }
        }
        catch
        {
            // la barra di Windows non e' affar nostro: se qualcosa non va,
            // meglio lasciarla com'e' che disturbare l'utente
        }
    }

    // --- rotellina ---------------------------------------------------------

    /// <summary>
    /// Gestisce un evento della rotellina. Restituisce true se l'abbiamo usato
    /// per sfogliare la barra (e quindi va consumato).
    /// </summary>
    public bool HandleWheel(int msg, int x, int y, uint mouseData)
    {
        if (_mode == TaskbarScrollMode.Off)
        {
            return false;
        }

        var wanted = _mode == TaskbarScrollMode.Wheel
            ? EffectsNative.WM_MOUSEWHEEL
            : EffectsNative.WM_MOUSEHWHEEL;
        if (msg != wanted)
        {
            return false;
        }

        try
        {
            var sw = TaskSwitcherAt(x, y);
            if (sw == IntPtr.Zero || !CanScroll(sw))
            {
                return false; // niente da sfogliare: la rotellina resta agli altri
            }

            var delta = EffectsNative.WheelDelta(mouseData);
            if (delta == 0)
            {
                return false;
            }

            // rotellina in su (o inclinata a sinistra) = pagina precedente
            var up = _mode == TaskbarScrollMode.Wheel ? delta > 0 : delta < 0;
            var cmd = up ? EffectsNative.SB_LINEUP : EffectsNative.SB_LINEDOWN;

            // Post e non Send: se Explorer e' impallato non ci blocchiamo dentro
            // l'hook di sistema (bloccarlo congelerebbe il mouse ovunque)
            EffectsNative.PostMessage(sw, EffectsNative.WM_VSCROLL, (IntPtr)cmd, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>MSTaskSwWClass sotto al punto, o zero se li' non c'e' la barra.</summary>
    private static IntPtr TaskSwitcherAt(int x, int y)
    {
        var hwnd = EffectsNative.WindowFromPoint(new EffectsNative.POINT { x = x, y = y });
        for (var h = hwnd; h != IntPtr.Zero; h = EffectsNative.GetParent(h))
        {
            if (ClassOf(h) == TaskSwClass)
            {
                return h;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>C'e' piu' di una pagina da sfogliare?</summary>
    private static bool CanScroll(IntPtr sw)
    {
        var si = new EffectsNative.SCROLLINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<EffectsNative.SCROLLINFO>(),
            fMask = EffectsNative.SIF_RANGE | EffectsNative.SIF_PAGE
        };
        if (!EffectsNative.GetScrollInfo(sw, EffectsNative.SB_VERT, ref si))
        {
            return false;
        }
        return si.nMax - si.nMin + 1 > si.nPage;
    }

    // --- freccette ---------------------------------------------------------

    private void HideScrollBar(IntPtr sw)
    {
        // tocca la finestra solo se le freccette ci sono davvero, cosi' non
        // bombardiamo Explorer di chiamate a ogni giro del timer
        if ((EffectsNative.GetWindowLong(sw, EffectsNative.GWL_STYLE) & EffectsNative.WS_VSCROLL) == 0)
        {
            return;
        }
        if (EffectsNative.ShowScrollBar(sw, EffectsNative.SB_VERT, false))
        {
            _hidden.Add(sw);
        }
    }

    private void RestoreScrollBars()
    {
        foreach (var sw in _hidden)
        {
            try
            {
                // rimettile solo se servono ancora: se nel frattempo le finestre
                // aperte stanno in una riga sola, una freccetta spenta sarebbe
                // peggio di niente
                if (EffectsNative.IsWindow(sw) && CanScroll(sw))
                {
                    EffectsNative.ShowScrollBar(sw, EffectsNative.SB_VERT, true);
                }
            }
            catch
            {
                // finestra sparita nel frattempo: nulla da rimettere
            }
        }
        _hidden.Clear();
    }

    // --- tinta -------------------------------------------------------------

    private void ApplyTint()
    {
        var abgr = ToAbgr(BarStyle.Mid);
        foreach (var tray in Trays())
        {
            SetAccent(tray, EffectsNative.ACCENT_ENABLE_GRADIENT, abgr);
        }
    }

    private void RestoreTint()
    {
        // Non proviamo a ricordarci lo stato di partenza:
        // GetWindowCompositionAttribute su una finestra di un altro processo
        // restituisce spazzatura (verificato), quindi "ripristinare" quel che
        // legge significherebbe inventarsi un colore. Chiediamo invece a
        // Explorer di riapplicare la sua policy, che e' l'unico che la conosce.
        foreach (var tray in Trays())
        {
            EffectsNative.SendMessageTimeout(tray, EffectsNative.WM_SETTINGCHANGE, IntPtr.Zero,
                "ImmersiveColorSet", EffectsNative.SMTO_ABORTIFHUNG, 1000, out _);
        }
    }

    private static void SetAccent(IntPtr hwnd, int state, uint gradientColor)
    {
        var policy = new EffectsNative.ACCENTPOLICY
        {
            AccentState = state,
            AccentFlags = EffectsNative.ACCENT_FLAG_ALL_BORDERS,
            GradientColor = gradientColor,
            AnimationId = 0
        };

        var size = System.Runtime.InteropServices.Marshal.SizeOf(policy);
        var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(policy, ptr, false);
            var data = new EffectsNative.WINCOMPATTRDATA
            {
                Attribute = EffectsNative.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size
            };
            EffectsNative.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// L'accent policy vuole ABGR, non ARGB. Tutto in aritmetica uint: passando
    /// per un int, 0xFF000000 sarebbe negativo e il cast salterebbe se un giorno
    /// il progetto compilasse in checked.
    /// </summary>
    private static uint ToAbgr(Color c) =>
        0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R;

    // --- ricerca finestre --------------------------------------------------

    /// <summary>La barra principale e quelle degli altri monitor.</summary>
    private static List<IntPtr> Trays()
    {
        var found = new List<IntPtr>();
        var primary = EffectsNative.FindWindow(TrayClass, null);
        if (primary != IntPtr.Zero)
        {
            found.Add(primary);
        }

        EffectsNative.EnumWindows((h, _) =>
        {
            if (ClassOf(h) == SecondaryTrayClass)
            {
                found.Add(h);
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>Gli elenchi applicazioni di tutte le barre.</summary>
    private static List<IntPtr> TaskSwitchers()
    {
        var found = new List<IntPtr>();
        foreach (var tray in Trays())
        {
            // EnumChildWindows scende in tutta la discendenza, quindi non
            // dipendiamo da come Explorer annida ReBarWindow32
            EffectsNative.EnumChildWindows(tray, (h, _) =>
            {
                if (ClassOf(h) == TaskSwClass)
                {
                    found.Add(h);
                }
                return true;
            }, IntPtr.Zero);
        }
        return found;
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        EffectsNative.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        // lascia la barra di Windows come l'abbiamo trovata
        try
        {
            RestoreScrollBars();
            if (_tint)
            {
                RestoreTint();
            }
        }
        catch
        {
            // in chiusura non c'e' nessuno a cui riportare l'errore
        }
    }
}
