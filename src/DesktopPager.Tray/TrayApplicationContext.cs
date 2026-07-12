using System.Drawing;
using System.Windows.Forms;

namespace DesktopPager.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DesktopPageManager _pageManager;
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly HotkeyWindow _window;

    public TrayApplicationContext()
    {
        var pagerService = new DesktopIconPagerService();
        _pageManager = new DesktopPageManager(maxPages: 10, iconsPerPage: 100, pagerService);
        _pageManager.RefreshState();
        _pageManager.EnsureBaselineLayout();

        _hotkeyManager = new GlobalHotkeyManager();
        _window = new HotkeyWindow(_hotkeyManager, _pageManager, UpdateTooltip);
        _hotkeyManager.Register(_window.Handle);

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Pagina successiva (Ctrl+Alt+PgUp)", null, (_, _) => ChangePage(_pageManager.NextPage));
        trayMenu.Items.Add("Pagina precedente (Ctrl+Alt+PgDn)", null, (_, _) => ChangePage(_pageManager.PreviousPage));
        trayMenu.Items.Add("Pagina principale (Ctrl+Alt+Fine)", null, (_, _) => ChangePage(_pageManager.GoToMainPage));
        trayMenu.Items.Add("Ripristina layout iniziale", null, (_, _) => _pageManager.RestoreBaselineLayout());
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
    }

    protected override void ExitThreadCore()
    {
        _pageManager.RestoreBaselineLayout();
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

    private void UpdateTooltip()
    {
        _notifyIcon.Text = $"DesktopPager - Pagina {_pageManager.CurrentPage}/{_pageManager.TotalPages}";
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly GlobalHotkeyManager _hotkeyManager;
        private readonly DesktopPageManager _pageManager;
        private readonly Action _onPageChanged;

        public HotkeyWindow(GlobalHotkeyManager hotkeyManager, DesktopPageManager pageManager, Action onPageChanged)
        {
            _hotkeyManager = hotkeyManager;
            _pageManager = pageManager;
            _onPageChanged = onPageChanged;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (_hotkeyManager.HandleMessage(m, _pageManager))
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
