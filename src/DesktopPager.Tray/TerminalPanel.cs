using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopPager.Tray;

/// <summary>
/// Terminale a tendina (stile Quake): occupa la meta' superiore dello schermo
/// e incorpora una vera console PowerShell o CMD (finestra conhost
/// re-parentata). Resta fissa finche' non viene chiusa con la X.
/// </summary>
public sealed class TerminalPanel : Form
{
    private const int HeaderHeight = 32;

    private Process? _process;
    private IntPtr _consoleWindow = IntPtr.Zero;
    private string _kind = "";
    private readonly Label _title;

    public TerminalPanel()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        Bounds = new Rectangle(screen.Left, screen.Top, screen.Width, screen.Height / 2);
        BackColor = ThemeService.BarBackground;

        _title = new Label
        {
            Text = "Terminale",
            ForeColor = ThemeService.BarForeground,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(12, 0, 400, HeaderHeight)
        };
        Controls.Add(_title);

        var close = new Button
        {
            Text = "✕",
            FlatStyle = FlatStyle.Flat,
            ForeColor = ThemeService.BarForeground,
            BackColor = ThemeService.BarBackground,
            Bounds = new Rectangle(Width - 44, 2, 38, HeaderHeight - 4),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TabStop = false
        };
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 30, 30);
        close.Click += (_, _) => CloseTerminal();
        Controls.Add(close);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: niente alt-tab
            return cp;
        }
    }

    /// <summary>Apre (o porta in primo piano) un terminale del tipo dato.</summary>
    public void ShowTerminal(string kind) // "powershell" | "cmd"
    {
        if (_process is { HasExited: false } && _kind == kind)
        {
            Show();
            BringToFront();
            return;
        }

        CloseTerminal();
        _kind = kind;
        _title.Text = kind == "cmd" ? "Prompt dei comandi" : "Windows PowerShell";
        BackColor = ThemeService.BarBackground;
        _title.ForeColor = ThemeService.BarForeground;

        try
        {
            // percorso completo di sistema (evita rischi di search-order)
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var exe = kind == "cmd"
                ? Path.Combine(sys, "cmd.exe")
                : Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized
            });
            if (_process is null)
            {
                return;
            }

            // aspetta che conhost crei la finestra della console
            for (var i = 0; i < 60 && _consoleWindow == IntPtr.Zero; i++)
            {
                Thread.Sleep(50);
                _process.Refresh();
                _consoleWindow = _process.MainWindowHandle;
            }

            if (_consoleWindow == IntPtr.Zero)
            {
                return;
            }

            Show();

            // incorpora la console nel pannello
            var style = GetWindowLongPtr(_consoleWindow, GwlStyle).ToInt64();
            style &= ~(long)(WsCaption | WsThickFrame);
            style |= WsChild;
            SetWindowLongPtr(_consoleWindow, GwlStyle, (IntPtr)style);
            SetParent(_consoleWindow, Handle);
            LayoutConsole();
            ShowWindow(_consoleWindow, 5 /*SW_SHOW*/);
            SetForegroundWindow(_consoleWindow);
        }
        catch
        {
            CloseTerminal();
        }
    }

    public void CloseTerminal()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill();
            }
        }
        catch
        {
            // gia' terminato
        }

        _process?.Dispose();
        _process = null;
        _consoleWindow = IntPtr.Zero;
        Hide();
    }

    private void LayoutConsole()
    {
        if (_consoleWindow != IntPtr.Zero)
        {
            MoveWindow(_consoleWindow, 0, HeaderHeight, ClientSize.Width, ClientSize.Height - HeaderHeight, true);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutConsole();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CloseTerminal();
        base.OnFormClosed(e);
    }

    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsChild = 0x40000000;

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
