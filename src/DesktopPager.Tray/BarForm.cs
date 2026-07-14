using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DesktopPager.Tray;

/// <summary>
/// Barra stile Windows a scomparsa. Di default sta in alto, ridotta a una
/// linguetta centrale che si espande al passaggio del mouse; i pulsanti
/// freccia alle estremita' la spostano sul lato sinistro o destro dello
/// schermo. Ospita avvii rapidi (anche via trascinamento), terminali
/// PowerShell/CMD a tendina e la vista 3D delle anteprime; i colori seguono
/// il tema di Windows.
/// </summary>
public sealed class BarForm : Form
{
    private enum Side { Top, Left, Right, Bottom }

    private const int Thickness = 48;
    private const int TabLength = 140;
    private const int TabThickness = 9;
    private const int ButtonSize = 38;

    // palette rosso scuro in rilievo (condivisa con il menu Start)
    private static readonly Color BarTop = BarStyle.Top;
    private static readonly Color BarBottom = BarStyle.Bottom;
    private static readonly Color BarMid = BarStyle.Mid;
    private static readonly Color BarHover = BarStyle.Hover;
    private static readonly Color BarHi = BarStyle.Highlight;
    private static readonly Color BarShadow = BarStyle.Shadow;
    private static readonly Color BarText = BarStyle.Text;

    private Side _side = Side.Top;
    private bool _expanded;
    private bool _replaceTaskbar;   // sostituisce la barra di Windows
    private bool _alwaysVisible;    // non collassare (modalità sostituzione)
    private int _pinned; // > 0 se un dialogo/menu e' aperto: non collassare

    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _clockTick = new() { Interval = 10_000 };
    private readonly TerminalPanel _terminal = new();

    private const int StartSize = 44;

    private readonly FlatIconButton _moveA = MakeButton("◀");
    private readonly FlatIconButton _moveB = MakeButton("▶");
    private readonly FlatIconButton _add = MakeButton("＋");
    private readonly FlatIconButton _ps = MakeButton("PS");
    private readonly FlatIconButton _cmd = MakeButton(">_");
    private readonly FlatIconButton _flow = MakeButton("3D");
    private readonly FlatIconButton _game = MakeButton("🎮");
    private readonly FlatIconButton _explorer = MakeButton("📁");
    private readonly FlatIconButton _apps = MakeButton("☰");
    private readonly FlatIconButton _power = MakeButton("⏻");
    private readonly PictureBox _start = new()
    {
        Size = new Size(StartSize, StartSize),
        SizeMode = PictureBoxSizeMode.Zoom,
        Cursor = Cursors.Hand
    };
    private readonly Label _clock = new()
    {
        AutoSize = false,
        Size = new Size(ButtonSize + 6, ButtonSize),
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold)
    };

    private readonly List<Control> _quickItems = new();
    private readonly ToolTip _tips = new();

    public BarForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AllowDrop = true;
        DoubleBuffered = true;

        _moveA.Click += (_, _) => MoveDock(first: true);
        _moveB.Click += (_, _) => MoveDock(first: false);
        _add.Click += (_, _) => AddShortcutViaDialog();
        _ps.Click += (_, _) => _terminal.ShowTerminal("powershell");
        _cmd.Click += (_, _) => _terminal.ShowTerminal("cmd");
        _flow.Click += (_, _) => OpenCoverFlow();
        _game.Click += (_, _) => OpenShop3D();
        _explorer.Click += (_, _) => Launch(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        _apps.Click += (_, _) => ShowOpenProgramsMenu();
        _power.Click += (_, _) => ShowPowerMenu();
        _start.Image = MakeWindowsCoin(StartSize * 2);
        _start.Click += (_, _) => ShowStartMenu();

        // icone nitide dal font di sistema (Segoe MDL2 Assets)
        _apps.Font = new Font("Segoe MDL2 Assets", 12f);
        _apps.Text = "";   // AllApps (programmi aperti)
        _power.Font = new Font("Segoe MDL2 Assets", 12f);
        _power.Text = "";  // PowerButton

        _tips.SetToolTip(_apps, "Programmi aperti");
        _tips.SetToolTip(_power, "Spegni / Riavvia / Sospendi / Blocca");
        _tips.SetToolTip(_start, "Menu di sistema");
        _tips.SetToolTip(_moveA, "Sposta la barra");
        _tips.SetToolTip(_moveB, "Sposta la barra");
        _tips.SetToolTip(_add, "Aggiungi collegamento");
        _tips.SetToolTip(_ps, "PowerShell a tendina");
        _tips.SetToolTip(_cmd, "Prompt dei comandi a tendina");
        _tips.SetToolTip(_flow, "Vista 3D anteprime file (cover flow)");
        _tips.SetToolTip(_game, "Vista 3D Game (esplora dischi e cartelle in prima persona)");
        _tips.SetToolTip(_explorer, "Esplora file");

        Controls.AddRange(new Control[] { _start, _moveA, _moveB, _add, _ps, _cmd, _flow, _game, _explorer, _apps, _power, _clock });

        _watch.Tick += (_, _) => CollapseIfIdle();
        _clockTick.Tick += (_, _) => _clock.Text = DateTime.Now.ToString("HH:mm");
        _clock.Text = DateTime.Now.ToString("HH:mm");
        _clockTick.Start();

        MouseEnter += (_, _) => { if (!_expanded) Expand(); };
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += OnFileDrop;

        // menu col tasto destro su tutta la barra
        HookRightClick(this);
        foreach (Control c in Controls)
        {
            HookRightClick(c);
        }

        Directory.CreateDirectory(QuickLaunchFolder);
        SeedDefaultShortcuts();
        ReloadQuickItems();
        Collapse();
        _watch.Start();
    }

    private void HookRightClick(Control c)
    {
        c.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowBarMenu();
            }
        };
    }

    public void ShowBarMenu()
    {
        var m = new ContextMenuStrip();
        var repl = new ToolStripMenuItem("Sostituisci la barra di Windows")
        {
            Checked = _replaceTaskbar,
            CheckOnClick = true
        };
        repl.Click += (_, _) => ToggleReplaceTaskbar();
        m.Items.Add(repl);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Aggancia in alto", null, (_, _) => { if (_replaceTaskbar) SetReplaceTaskbar(false); _side = Side.Top; Expand(); });
        m.Items.Add("Aggancia a sinistra", null, (_, _) => { if (_replaceTaskbar) SetReplaceTaskbar(false); _side = Side.Left; Expand(); });
        m.Items.Add("Aggancia a destra", null, (_, _) => { if (_replaceTaskbar) SetReplaceTaskbar(false); _side = Side.Right; Expand(); });

        _pinned++;
        m.Closed += (_, _) => _pinned--;
        SetForegroundWindow(Handle);
        Activate();
        m.Show(Cursor.Position);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    private static string QuickLaunchFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopPager3D-OS", "QuickLaunch");

    private static FlatIconButton MakeButton(string text)
    {
        return new FlatIconButton
        {
            Text = text,
            Size = new Size(ButtonSize, ButtonSize),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = BarStyle.Text,
            HoverColor = BarStyle.Hover,
            Margin = Padding.Empty
        };
    }

    // --- espansione / linguetta -----------------------------------------

    private Rectangle EdgeBounds()
    {
        var scr = Screen.PrimaryScreen!;
        // in modalità sostituzione usa tutto lo schermo (copre la barra di Windows)
        var wa = _replaceTaskbar ? scr.Bounds : scr.WorkingArea;
        return _side switch
        {
            Side.Left => new Rectangle(wa.Left, wa.Top, Thickness, wa.Height),
            Side.Right => new Rectangle(wa.Right - Thickness, wa.Top, Thickness, wa.Height),
            Side.Bottom => new Rectangle(wa.Left, wa.Bottom - Thickness, wa.Width, Thickness),
            _ => new Rectangle(wa.Left, wa.Top, wa.Width, Thickness)
        };
    }

    private Rectangle TabBounds()
    {
        var scr = Screen.PrimaryScreen!;
        var wa = _replaceTaskbar ? scr.Bounds : scr.WorkingArea;
        return _side switch
        {
            Side.Left => new Rectangle(wa.Left, wa.Top + (wa.Height - TabLength) / 2, TabThickness, TabLength),
            Side.Right => new Rectangle(wa.Right - TabThickness, wa.Top + (wa.Height - TabLength) / 2, TabThickness, TabLength),
            Side.Bottom => new Rectangle(wa.Left + (wa.Width - TabLength) / 2, wa.Bottom - TabThickness, TabLength, TabThickness),
            _ => new Rectangle(wa.Left + (wa.Width - TabLength) / 2, wa.Top, TabLength, TabThickness)
        };
    }

    private void Expand()
    {
        _expanded = true;
        ApplyTheme();
        Bounds = EdgeBounds();
        LayoutControls();
        foreach (Control c in Controls) c.Visible = true;
        Invalidate();
    }

    private void Collapse()
    {
        _expanded = false;
        foreach (Control c in Controls) c.Visible = false;
        Bounds = TabBounds();
        Invalidate();
    }

    private void CollapseIfIdle()
    {
        if (_expanded && !_alwaysVisible && _pinned == 0 && !Bounds.Contains(Cursor.Position))
        {
            Collapse();
        }
    }

    private void ApplyTheme()
    {
        BackColor = BarTop;
        foreach (Control c in Controls)
        {
            c.ForeColor = BarText;
            if (c is FlatIconButton fb)
            {
                fb.HoverColor = BarHover; // trasparente: nessuna tessera quadrata
            }
            else
            {
                c.BackColor = Color.Transparent; // etichette/icone: lascia vedere il rilievo
            }
        }
        _start.BackColor = Color.Transparent;
    }

    // --- layout ----------------------------------------------------------

    private void LayoutControls()
    {
        var horizontal = _side is Side.Top or Side.Bottom;
        UpdateMoveGlyphs();

        var head = new List<Control> { _moveA };
        var tail = new List<Control> { _explorer, _ps, _cmd, _flow, _game, _apps, _power, _clock, _moveB };
        var middle = new List<Control>(_quickItems) { _add };

        var pad = 5;
        if (horizontal)
        {
            var x = pad;
            foreach (var c in head) { c.Location = new Point(x, (Thickness - c.Height) / 2); x += c.Width + pad; }
            foreach (var c in middle) { c.Location = new Point(x, (Thickness - c.Height) / 2); x += c.Width + pad; }
            var xr = Width - pad;
            for (var i = tail.Count - 1; i >= 0; i--)
            {
                xr -= tail[i].Width;
                tail[i].Location = new Point(xr, (Thickness - tail[i].Height) / 2);
                xr -= pad;
            }
            // moneta Windows al centro della barra
            _start.Location = new Point((Width - _start.Width) / 2, (Thickness - _start.Height) / 2);
        }
        else
        {
            var y = pad;
            foreach (var c in head) { c.Location = new Point((Thickness - c.Width) / 2, y); y += c.Height + pad; }
            foreach (var c in middle) { c.Location = new Point((Thickness - c.Width) / 2, y); y += c.Height + pad; }
            var yb = Height - pad;
            for (var i = tail.Count - 1; i >= 0; i--)
            {
                yb -= tail[i].Height;
                tail[i].Location = new Point((Thickness - tail[i].Width) / 2, yb);
                yb -= pad;
            }
            _start.Location = new Point((Thickness - _start.Width) / 2, (Height - _start.Height) / 2);
        }
    }

    private void UpdateMoveGlyphs()
    {
        switch (_side)
        {
            case Side.Top:
            case Side.Bottom:
                _moveA.Text = "◀"; _tips.SetToolTip(_moveA, "Aggancia a sinistra");
                _moveB.Text = "▶"; _tips.SetToolTip(_moveB, "Aggancia a destra");
                break;
            case Side.Left:
                _moveA.Text = "▲"; _tips.SetToolTip(_moveA, "Aggancia in alto");
                _moveB.Text = "▶"; _tips.SetToolTip(_moveB, "Aggancia a destra");
                break;
            default:
                _moveA.Text = "▲"; _tips.SetToolTip(_moveA, "Aggancia in alto");
                _moveB.Text = "◀"; _tips.SetToolTip(_moveB, "Aggancia a sinistra");
                break;
        }
    }

    private void MoveDock(bool first)
    {
        // spostando la barra si esce dalla modalità sostituzione
        if (_replaceTaskbar)
        {
            SetReplaceTaskbar(false);
        }

        _side = (_side, first) switch
        {
            (Side.Top, true) => Side.Left,
            (Side.Top, false) => Side.Right,
            (Side.Left, true) => Side.Top,
            (Side.Left, false) => Side.Right,
            (Side.Right, true) => Side.Top,
            _ => Side.Left
        };
        Expand();
    }

    // --- sostituzione della barra di Windows ------------------------------

    public bool ReplaceTaskbarEnabled => _replaceTaskbar;

    public void ToggleReplaceTaskbar() => SetReplaceTaskbar(!_replaceTaskbar);

    private void SetReplaceTaskbar(bool on)
    {
        _replaceTaskbar = on;
        _alwaysVisible = on;
        ShowWindowsTaskbar(!on);   // nasconde/mostra la barra di Windows

        if (on)
        {
            _side = Side.Bottom;
            Expand();
            _moveA.Visible = false;
            _moveB.Visible = false;
            TopMost = true;
        }
        else
        {
            _side = Side.Top;
            _moveA.Visible = true;
            _moveB.Visible = true;
            Collapse();
        }
    }

    private static void ShowWindowsTaskbar(bool visible)
    {
        var cmd = visible ? 5 : 0; // SW_SHOW : SW_HIDE
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero)
        {
            ShowWindow(tray, cmd);
        }

        // eventuali barre secondarie (multi-monitor)
        var sec = IntPtr.Zero;
        while ((sec = FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            ShowWindow(sec, cmd);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // --- avvii rapidi ------------------------------------------------------

    private void ReloadQuickItems()
    {
        foreach (var c in _quickItems)
        {
            Controls.Remove(c);
            c.Dispose();
        }
        _quickItems.Clear();

        foreach (var file in Directory.EnumerateFiles(QuickLaunchFolder).OrderBy(f => f).Take(24))
        {
            var pb = new FlatIconButton
            {
                Size = new Size(ButtonSize, ButtonSize),
                Tag = file,
                HoverColor = BarStyle.Hover,
                Visible = _expanded
            };
            try
            {
                // estrai l'icona del bersaglio, non del .lnk, cosi' non
                // compare la freccetta di overlay dei collegamenti
                var iconSource = ResolveShortcutTarget(file) ?? file;
                using var icon = Icon.ExtractAssociatedIcon(iconSource);
                if (icon is not null)
                {
                    pb.Image = IconToTransparentBitmap(icon, 28);
                }
            }
            catch
            {
                // niente icona: resta vuoto ma cliccabile
            }
            _tips.SetToolTip(pb, Path.GetFileNameWithoutExtension(file));
            pb.MouseClick += OnQuickItemClick;
            Controls.Add(pb);
            _quickItems.Add(pb);
        }
    }

    // converte un'icona in bitmap con sfondo trasparente (niente quadratino nero):
    // ToBitmap() conserva l'alfa, DrawImage (GDI+) la preserva nel ridimensionare
    private static Bitmap IconToTransparentBitmap(Icon icon, int size)
    {
        using var src = icon.ToBitmap();
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, new Rectangle(0, 0, size, size));
        return bmp;
    }

    private void OnQuickItemClick(object? sender, MouseEventArgs e)
    {
        if (sender is not FlatIconButton pb || pb.Tag is not string file)
        {
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            Launch(file);
        }
        else if (e.Button == MouseButtons.Right)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Rimuovi dalla barra", null, (_, _) =>
            {
                try { File.Delete(file); } catch { }
                ReloadQuickItems();
                LayoutControls();
            });
            menu.Items.Add("Apri cartella avvii rapidi", null, (_, _) => Launch(QuickLaunchFolder));
            _pinned++;
            menu.Closed += (_, _) => _pinned--;
            menu.Show(Cursor.Position);
        }
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files)
            {
                AddShortcut(f);
            }
            ReloadQuickItems();
            LayoutControls();
        }
    }

    private void AddShortcutViaDialog()
    {
        _pinned++;
        try
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Scegli il programma o file da aggiungere alla barra",
                Filter = "Tutti i file|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                AddShortcut(dlg.FileName);
                ReloadQuickItems();
                LayoutControls();
            }
        }
        finally
        {
            _pinned--;
        }
    }

    private void AddShortcut(string source)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(source);
            if (Path.GetExtension(source).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, Path.Combine(QuickLaunchFolder, name + ".lnk"), overwrite: true);
                return;
            }

            CreateLnk(Path.Combine(QuickLaunchFolder, name + ".lnk"), source);
        }
        catch
        {
            // aggiunta fallita: ignora
        }
    }

    private static string? ResolveShortcutTarget(string lnkPath)
    {
        if (!Path.GetExtension(lnkPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            var lnk = shell.CreateShortcut(lnkPath);
            string target = lnk.TargetPath;
            return string.IsNullOrWhiteSpace(target) || !File.Exists(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    private static void CreateLnk(string lnkPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        var lnk = shell.CreateShortcut(lnkPath);
        lnk.TargetPath = target;
        lnk.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
        lnk.Save();
    }

    private void SeedDefaultShortcuts()
    {
        if (Directory.EnumerateFileSystemEntries(QuickLaunchFolder).Any())
        {
            return;
        }

        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var exe in new[] { Path.Combine(win, "explorer.exe"), Path.Combine(win, "notepad.exe") })
        {
            if (File.Exists(exe))
            {
                AddShortcut(exe);
            }
        }
    }

    private static void Launch(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = file, UseShellExecute = true });
        }
        catch
        {
            // avvio fallito: ignora
        }
    }

    // --- moneta Windows e menu di sistema ---------------------------------

    private static Bitmap MakeWindowsCoin(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var rect = new Rectangle(1, 1, size - 2, size - 2);

        // corpo della moneta con gradiente radiale (rilievo tondeggiante)
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            using var pgb = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(size * 0.38f, size * 0.34f),
                CenterColor = Color.FromArgb(255, 250, 250, 252),
                SurroundColors = new[] { Color.FromArgb(255, 148, 156, 168) }
            };
            g.FillEllipse(pgb, rect);
        }
        using (var pen = new Pen(Color.FromArgb(255, 88, 94, 106), Math.Max(1f, size / 32f)))
        {
            g.DrawEllipse(pen, rect);
        }

        // logo Windows a 4 colori (stile Windows 7)
        var s = size * 0.17f;
        var gap = size * 0.05f;
        var cx = size / 2f;
        var cy = size / 2f;
        Color[] cols =
        {
            Color.FromArgb(0xE6, 0x3B, 0x2E), Color.FromArgb(0x6F, 0xBF, 0x2E),
            Color.FromArgb(0x00, 0x9D, 0xE0), Color.FromArgb(0xF7, 0xB6, 0x00)
        };
        (float x, float y)[] pos =
        {
            (cx - s - gap / 2, cy - s - gap / 2), (cx + gap / 2, cy - s - gap / 2),
            (cx - s - gap / 2, cy + gap / 2), (cx + gap / 2, cy + gap / 2)
        };
        for (var i = 0; i < 4; i++)
        {
            using var b = new SolidBrush(cols[i]);
            g.FillRectangle(b, pos[i].x, pos[i].y, s, s);
        }
        return bmp;
    }

    private void ShowStartMenu()
    {
        try
        {
            ShowStartMenuCore();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Errore menu Start:\n" + ex, "DesktopPager3D-OS");
        }
    }

    private void ShowStartMenuCore()
    {
        // menu Start in stile Windows con le applicazioni installate per categoria
        var menu = new StartMenuForm();

        // posiziona la finestra vicino alla moneta, dentro lo schermo
        var coinScreen = _start.PointToScreen(Point.Empty);
        var wa = Screen.PrimaryScreen!.WorkingArea;
        int x, y;
        if (_side == Side.Top)
        {
            x = coinScreen.X + _start.Width / 2 - menu.Width / 2;
            y = Bounds.Bottom;
        }
        else if (_side == Side.Left)
        {
            x = Bounds.Right;
            y = coinScreen.Y - menu.Height / 2;
        }
        else
        {
            x = Bounds.Left - menu.Width;
            y = coinScreen.Y - menu.Height / 2;
        }
        x = Math.Clamp(x, wa.Left, wa.Right - menu.Width);
        y = Math.Clamp(y, wa.Top, wa.Bottom - menu.Height);
        menu.Location = new Point(x, y);

        _pinned++;
        menu.FormClosed += (_, _) => _pinned--;
        SetForegroundWindow(Handle);
        Activate();
        menu.Show();
        SetForegroundWindow(menu.Handle);
        menu.Activate();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // --- programmi aperti --------------------------------------------------

    private void ShowOpenProgramsMenu()
    {
        var m = new ContextMenuStrip();
        var wins = EnumOpenWindows();
        if (wins.Count == 0)
        {
            var none = m.Items.Add("(nessun programma aperto)");
            none.Enabled = false;
        }
        else
        {
            foreach (var (hwnd, title) in wins)
            {
                var t = title.Length > 60 ? title[..60] + "…" : title;
                var h = hwnd;
                var item = m.Items.Add(t);
                try
                {
                    item.Image = WindowIcon(h);
                }
                catch
                {
                    // niente icona
                }
                item.Click += (_, _) => ActivateWindow(h);
            }
        }

        _pinned++;
        m.Closed += (_, _) => _pinned--;
        SetForegroundWindow(Handle);
        Activate();
        m.Show(Cursor.Position);
    }

    private List<(IntPtr hwnd, string title)> EnumOpenWindows()
    {
        var list = new List<(IntPtr, string)>();
        var self = Handle;
        EnumWindows((h, _) =>
        {
            if (h == self || !IsWindowVisible(h))
            {
                return true;
            }

            if (GetWindow(h, GW_OWNER) != IntPtr.Zero)
            {
                return true; // finestra di proprietà (dialogo): salta
            }

            var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0)
            {
                return true; // tool window: non è un'app
            }

            var len = GetWindowTextLength(h);
            if (len == 0)
            {
                return true;
            }

            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (!string.IsNullOrWhiteSpace(title))
            {
                list.Add((h, title));
            }

            return true;
        }, IntPtr.Zero);

        return list.OrderBy(w => w.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static Bitmap? WindowIcon(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            var path = p.MainModule?.FileName;
            if (!string.IsNullOrEmpty(path))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    return IconToTransparentBitmap(icon, 16);
                }
            }
        }
        catch
        {
            // accesso al processo negato: niente icona
        }

        return null;
    }

    private static void ActivateWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, 9); // SW_RESTORE
        }

        SetForegroundWindow(hwnd);
    }

    // --- spegnimento -------------------------------------------------------

    private void ShowPowerMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add("Sospendi", null, (_, _) => Suspend());
        m.Items.Add("Blocca", null, (_, _) => LockWorkStation());
        m.Items.Add("Disconnetti", null, (_, _) =>
        {
            if (Confirm("Disconnettere l'utente? I programmi aperti verranno chiusi."))
            {
                RunShutdown("/l");
            }
        });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Riavvia", null, (_, _) =>
        {
            if (Confirm("Riavviare il computer?"))
            {
                RunShutdown("/r /t 0");
            }
        });
        m.Items.Add("Arresta (spegni)", null, (_, _) =>
        {
            if (Confirm("Arrestare (spegnere) il computer?"))
            {
                RunShutdown("/s /t 0");
            }
        });

        _pinned++;
        m.Closed += (_, _) => _pinned--;
        SetForegroundWindow(Handle);
        Activate();
        m.Show(Cursor.Position);
    }

    private static bool Confirm(string message) =>
        MessageBox.Show(message, "DesktopPager3D-OS", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private static void RunShutdown(string args)
    {
        try
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
            Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, CreateNoWindow = true });
        }
        catch
        {
            // spegnimento fallito: ignora
        }
    }

    private static void Suspend()
    {
        try
        {
            SetSuspendState(false, false, false); // sospensione (non ibernazione)
        }
        catch
        {
            // sospensione non disponibile
        }
    }

    private const int GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [System.Runtime.InteropServices.DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private void OpenShop3D()
    {
        try
        {
            new Shop3DWindow().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossibile aprire la Vista 3D Game:\n" + ex.Message,
                "DesktopPager3D-OS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OpenCoverFlow()
    {
        _pinned++;
        try
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Scegli la cartella per la vista 3D delle anteprime",
                SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    // 3D reale accelerato (WPF); fallback GDI+ se non disponibile
                    var win = new CoverFlow3DWindow(dlg.SelectedPath);
                    win.Show();
                }
                catch
                {
                    new CoverFlowForm(dlg.SelectedPath).Show();
                }
            }
        }
        finally
        {
            _pinned--;
        }
    }

    // --- disegno linguetta -------------------------------------------------

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0)
        {
            base.OnPaintBackground(e);
            return;
        }

        // gradiente per l'effetto in rilievo (chiaro sopra -> scuro sotto)
        var vertical = _side != Side.Left && _side != Side.Right;
        using var lg = new LinearGradientBrush(r, BarTop, BarBottom, vertical ? 90f : 0f);
        e.Graphics.FillRectangle(lg, r);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var r = ClientRectangle;
        var horizontal = _side is Side.Top or Side.Bottom;

        // bisello: bordo chiaro sul lato "in luce", ombra sul lato opposto
        using var hi = new Pen(BarHi, 1f);
        using var sh = new Pen(BarShadow, 2f);
        if (horizontal)
        {
            g.DrawLine(hi, 0, 0, r.Width, 0);
            g.DrawLine(sh, 0, r.Bottom - 1, r.Width, r.Bottom - 1);
        }
        else if (_side == Side.Left)
        {
            g.DrawLine(hi, 0, 0, 0, r.Height);
            g.DrawLine(sh, r.Right - 1, 0, r.Right - 1, r.Height);
        }
        else
        {
            g.DrawLine(hi, r.Right - 1, 0, r.Right - 1, r.Height);
            g.DrawLine(sh, 0, 0, 0, r.Height);
        }

        if (_expanded)
        {
            return;
        }

        // linguetta chiusa: tre puntini bianchi (lo sfondo è già il gradiente rosso)
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var dots = new SolidBrush(Color.White);
        var cx = Width / 2;
        var cy = Height / 2;
        for (var i = -1; i <= 1; i++)
        {
            var p = horizontal ? new Point(cx + i * 12, cy) : new Point(cx, cy + i * 12);
            g.FillEllipse(dots, p.X - 2, p.Y - 2, 4, 4);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _watch.Stop();
        _clockTick.Stop();
        _terminal.CloseTerminal();
        _terminal.Dispose();
        if (_replaceTaskbar)
        {
            ShowWindowsTaskbar(true); // non lasciare la barra di Windows nascosta
        }
        base.OnFormClosed(e);
    }
}
