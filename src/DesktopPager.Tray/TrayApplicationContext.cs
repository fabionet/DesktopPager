using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DesktopPager.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DesktopPageManager _pageManager;
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly AutostartService _autostartService;
    private readonly HotkeyWindow _window;
    private readonly BarForm _bar;

    public TrayApplicationContext()
    {
        var pagerService = new DesktopIconPagerService();
        _pageManager = new DesktopPageManager(pagerService);
        _pageManager.RefreshState();
        _autostartService = new AutostartService();

        _hotkeyManager = new GlobalHotkeyManager();
        _window = new HotkeyWindow(_hotkeyManager, _pageManager, UpdateTooltip, RestartExplorer, RotateScreen);
        var hotkeysRegistered = _hotkeyManager.Register(_window.Handle);

        // barra a scomparsa stile Windows, visibile all'avvio
        _bar = new BarForm();
        _bar.Show();

        var trayMenu = new ContextMenuStrip();
        var barItem = new ToolStripMenuItem("Barra a scomparsa") { Checked = true, CheckOnClick = true };
        barItem.CheckedChanged += (_, _) => { if (barItem.Checked) _bar.Show(); else _bar.Hide(); };
        trayMenu.Items.Add(barItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Pagina avanti (Ctrl+Alt+PgGiù)", null, (_, _) => ChangePage(_pageManager.NextPage));
        trayMenu.Items.Add("Pagina indietro (Ctrl+Alt+PgSu)", null, (_, _) => ChangePage(_pageManager.PreviousPage));
        trayMenu.Items.Add("Prima pagina (Ctrl+Alt+Home)", null, (_, _) => ChangePage(_pageManager.GoToMainPage));
        var rotateMenu = new ToolStripMenuItem("Ruota schermo");
        rotateMenu.DropDownItems.Add("Sottosopra (Ctrl+Alt+Su)", null, (_, _) => RotateScreen(ScreenRotationService.Orientation180));
        rotateMenu.DropDownItems.Add("Normale (Ctrl+Alt+Giù)", null, (_, _) => RotateScreen(ScreenRotationService.OrientationDefault));
        rotateMenu.DropDownItems.Add("Barra a sinistra (Ctrl+Alt+Sinistra)", null, (_, _) => RotateScreen(ScreenRotationService.Orientation90));
        rotateMenu.DropDownItems.Add("Barra a destra (Ctrl+Alt+Destra)", null, (_, _) => RotateScreen(ScreenRotationService.Orientation270));
        rotateMenu.DropDownItems.Add(new ToolStripSeparator());
        rotateMenu.DropDownItems.Add("Emergenza: ripristina (Ctrl+Alt+Shift+^)", null, (_, _) => RotateScreen(ScreenRotationService.OrientationDefault));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(rotateMenu);
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
        _bar.Close();
        _bar.Dispose();
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

    private void RotateScreen(int orientation)
    {
        if (!ScreenRotationService.Rotate(orientation))
        {
            _notifyIcon.ShowBalloonTip(
                4000,
                "DesktopPager",
                "Rotazione non riuscita: il driver video potrebbe non supportarla.",
                ToolTipIcon.Warning);
        }
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
        private readonly Action<int> _rotateScreen;

        public HotkeyWindow(GlobalHotkeyManager hotkeyManager, DesktopPageManager pageManager, Action onPageChanged, Action restartExplorer, Action<int> rotateScreen)
        {
            _hotkeyManager = hotkeyManager;
            _pageManager = pageManager;
            _onPageChanged = onPageChanged;
            _restartExplorer = restartExplorer;
            _rotateScreen = rotateScreen;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (_hotkeyManager.HandleMessage(m, _pageManager, _restartExplorer, _rotateScreen))
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
