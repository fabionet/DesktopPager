using System.Drawing;
using System.Windows.Forms;

namespace DesktopPager.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DesktopPageManager _pageManager;
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly AutostartService _autostartService;
    private readonly HotkeyWindow _window;

    public TrayApplicationContext()
    {
        var pagerService = new DesktopIconPagerService();
        _pageManager = new DesktopPageManager(pagerService);
        _pageManager.RefreshState();
        _autostartService = new AutostartService();

        _hotkeyManager = new GlobalHotkeyManager();
        _window = new HotkeyWindow(_hotkeyManager, _pageManager, UpdateTooltip, RestartExplorer);
        var hotkeysRegistered = _hotkeyManager.Register(_window.Handle);

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Pagina avanti (Ctrl+Alt+PgGiù)", null, (_, _) => ChangePage(_pageManager.NextPage));
        trayMenu.Items.Add("Pagina indietro (Ctrl+Alt+PgSu)", null, (_, _) => ChangePage(_pageManager.PreviousPage));
        trayMenu.Items.Add("Prima pagina (Ctrl+Alt+Home)", null, (_, _) => ChangePage(_pageManager.GoToMainPage));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Riavvia Explorer (Ctrl+Alt+Fine)", null, (_, _) => RestartExplorer());
        var autostartItem = new ToolStripMenuItem("Avvio automatico con Windows")
        {
            Checked = _autostartService.IsEnabled()
        };
        autostartItem.Click += (_, _) =>
        {
            var targetEnabled = !autostartItem.Checked;
            if (_autostartService.SetEnabled(targetEnabled))
            {
                autostartItem.Checked = targetEnabled;
            }
            else
            {
                MessageBox.Show(
                    "Impossibile aggiornare l'impostazione di avvio automatico.",
                    "DesktopPager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        trayMenu.Items.Add(autostartItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Esci", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = ResolveIcon(),
            Visible = true,
            Text = "DesktopPager",
            ContextMenuStrip = trayMenu
        };
        UpdateTooltip();
        if (!hotkeysRegistered)
        {
            _notifyIcon.ShowBalloonTip(
                5000,
                "DesktopPager",
                "Impossibile registrare una o più hotkey globali. Verifica conflitti con altre applicazioni.",
                ToolTipIcon.Warning);
        }
    }

    protected override void ExitThreadCore()
    {
        // lascia il desktop come l'abbiamo trovato (scroll a zero, stile originale)
        _pageManager.GoToMainPage();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _hotkeyManager.Dispose();
        _window.Dispose();
        base.ExitThreadCore();
    }

    private static Icon ResolveIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DesktopPager.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private bool ChangePage(Func<bool> action)
    {
        var ok = action();
        UpdateTooltip();
        return ok;
    }

    private void RestartExplorer()
    {
        if (!ExplorerService.Restart())
        {
            MessageBox.Show(
                "Impossibile riavviare Explorer.",
                "DesktopPager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        _pageManager.RefreshState();
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        _notifyIcon.Text = $"DesktopPager - Pagina {_pageManager.CurrentPage}/{_pageManager.TotalPages}";
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly GlobalHotkeyManager _hotkeyManager;
        private readonly DesktopPageManager _pageManager;
        private readonly Action _onPageChanged;
        private readonly Action _restartExplorer;

        public HotkeyWindow(GlobalHotkeyManager hotkeyManager, DesktopPageManager pageManager, Action onPageChanged, Action restartExplorer)
        {
            _hotkeyManager = hotkeyManager;
            _pageManager = pageManager;
            _onPageChanged = onPageChanged;
            _restartExplorer = restartExplorer;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (_hotkeyManager.HandleMessage(m, _pageManager, _restartExplorer))
            {
                _onPageChanged();
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}
