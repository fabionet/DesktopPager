using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopPager.Tray;

/// <summary>
/// Menu Start in stile Windows: due colonne. A sinistra "Tutti i programmi"
/// con le applicazioni realmente installate (le stesse del menu Start di
/// Windows: cartelle del menu di tutti gli utenti + dell'utente corrente),
/// organizzate per categoria. A destra i collegamenti classici (cartelle
/// utente, Pannello di controllo, Strumenti di amministrazione, terminali).
/// </summary>
public sealed class StartMenuForm : Form
{
    private readonly TreeView _tree = new();
    private readonly ImageList _icons = new() { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
    private readonly TextBox _search = new();
    private readonly ToolTip _tips = new();

    public StartMenuForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(700, 600);
        var back = ThemeService.BarBackground;
        var fore = ThemeService.BarForeground;
        BackColor = back;

        // bordo sottile color accento
        Padding = new Padding(1);
        var accent = ThemeService.Accent;

        // intestazione
        var header = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = accent };
        var title = new Label
        {
            Text = "  DesktopPager3D-OS  •  " + Environment.UserName,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(title);

        // colonna destra: collegamenti classici
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 240,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = ThemeService.IsDarkTheme() ? ControlDark(back) : ControlLight(back),
            Padding = new Padding(10, 12, 10, 10)
        };
        AddPlaces(right, fore);

        // colonna sinistra: tutti i programmi
        var left = new Panel { Dock = DockStyle.Fill, BackColor = back, Padding = new Padding(8, 8, 4, 8) };
        var allLabel = new Label
        {
            Text = "Tutti i programmi",
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = fore,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        _tree.Dock = DockStyle.Fill;
        _tree.BorderStyle = BorderStyle.None;
        _tree.BackColor = back;
        _tree.ForeColor = fore;
        _tree.HideSelection = false;
        _tree.ShowLines = false;
        _tree.ShowRootLines = false;
        _tree.ShowPlusMinus = true;
        _tree.ItemHeight = 22;
        _tree.FullRowSelect = true;
        _tree.ImageList = _icons;
        _tree.NodeMouseClick += OnNodeClick;
        _tree.KeyDown += OnTreeKey;
        _tree.BeforeExpand += (_, e) => LoadIconsFor(e.Node.Nodes);
        left.Controls.Add(_tree);
        left.Controls.Add(allLabel);

        // barra inferiore: ricerca
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = back, Padding = new Padding(10, 6, 10, 8) };
        _search.Dock = DockStyle.Fill;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.BackColor = ThemeService.IsDarkTheme() ? ControlDark(back) : Color.White;
        _search.ForeColor = fore;
        _search.Font = new Font("Segoe UI", 10f);
        _search.PlaceholderText = "Cerca programmi…";
        _search.TextChanged += (_, _) => Populate(_search.Text.Trim());
        bottom.Controls.Add(_search);

        Controls.Add(left);
        Controls.Add(right);
        Controls.Add(bottom);
        Controls.Add(header);

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        // mostra prima la finestra vuota, poi popola (così non blocca l'apertura)
        Shown += (_, _) =>
        {
            BeginInvoke(new Action(() => Populate("")));

            // aggancia la chiusura-su-deactivate solo dopo la comparsa
            var arm = new System.Windows.Forms.Timer { Interval = 600 };
            arm.Tick += (_, _) =>
            {
                arm.Stop();
                arm.Dispose();
                Deactivate += (_, _) => Close();
            };
            arm.Start();
        };
    }

    private static Color ControlDark(Color c) => Color.FromArgb(c.R + 12, c.G + 12, c.B + 14);
    private static Color ControlLight(Color c) => Color.FromArgb(Math.Max(0, c.R - 12), Math.Max(0, c.G - 12), Math.Max(0, c.B - 12));

    // --- colonna destra (collegamenti classici) ---------------------------

    private void AddPlaces(FlowLayoutPanel panel, Color fore)
    {
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string Sys(string f) => Path.Combine(sys, f);
        var powershell = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        void Link(string text, Action action) => panel.Controls.Add(MakeLink(text, fore, action));
        void Sep() => panel.Controls.Add(new Panel { Height = 8, Width = 210 });

        Link(Environment.UserName, () => Launch(profile));
        Sep();
        Link("Documenti", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
        Link("Immagini", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)));
        Link("Musica", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)));
        Link("Video", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)));
        Link("Download", () => Launch(Path.Combine(profile, "Downloads")));
        Sep();
        Link("Questo PC", () => LaunchArgs(Path.Combine(win, "explorer.exe"), "shell:MyComputerFolder"));
        Link("Rete", () => LaunchArgs(Path.Combine(win, "explorer.exe"), "shell:NetworkPlacesFolder"));
        Sep();
        Link("Pannello di controllo", () => Launch(Sys("control.exe")));
        Link("Dispositivi e stampanti", () => LaunchArgs(Sys("control.exe"), "printers"));
        Link("Strumenti di amministrazione", () => LaunchArgs(Sys("control.exe"), "admintools"));
        Sep();
        Link("Prompt dei comandi", () => Launch(Sys("cmd.exe")));
        Link("Windows PowerShell", () => Launch(powershell));
        Link("Gestione attività", () => Launch(Sys("taskmgr.exe")));
    }

    private Button MakeLink(string text, Color fore, Action action)
    {
        var b = new Button
        {
            Text = text,
            Width = 214,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            ForeColor = fore,
            BackColor = BackColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(0, 1, 0, 1),
            TabStop = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ThemeService.ButtonHover;
        b.Click += (_, _) => { action(); Close(); };
        return b;
    }

    // --- colonna sinistra (tutti i programmi) -----------------------------

    private void Populate(string filter)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        if (_icons.Images.Count == 0)
        {
            _icons.Images.Add("__folder", FolderIcon());
            _icons.Images.Add("__app", GenericAppIcon());
        }

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
        };
        foreach (var root in roots)
        {
            if (Directory.Exists(root))
            {
                AddDir(_tree.Nodes, root, filter);
            }
        }

        PruneEmpty(_tree.Nodes);

        if (!string.IsNullOrEmpty(filter))
        {
            _tree.ExpandAll();
        }
        else if (_tree.Nodes.Count > 0)
        {
            _tree.Nodes[0].EnsureVisible();
        }

        _tree.EndUpdate();
    }

    // carica le icone (SHGetFileInfo) per i nodi indicati: chiamato quando si
    // espande una categoria, così si estraggono solo le poche icone visibili
    private void LoadIconsFor(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is string file && n.ImageKey == "__app")
            {
                if (!_icons.Images.ContainsKey(file))
                {
                    var bmp = ShellSmallIcon(file);
                    if (bmp is null)
                    {
                        continue;
                    }
                    _icons.Images.Add(file, bmp);
                }

                n.ImageKey = file;
                n.SelectedImageKey = file;
            }
        }
    }

    private static Bitmap? ShellSmallIcon(string path)
    {
        var sfi = new SHFILEINFO();
        var res = SHGetFileInfo(path, 0, ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
        if (res == IntPtr.Zero || sfi.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var ic = Icon.FromHandle(sfi.hIcon);
            return new Bitmap(ic.ToBitmap(), 16, 16);
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(sfi.hIcon);
        }
    }

    private void AddDir(TreeNodeCollection parent, string dir, string filter)
    {
        try
        {
            foreach (var sub in Directory.GetDirectories(dir).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileName(sub);
                var node = FindFolder(parent, name) ?? AddFolder(parent, name);
                AddDir(node.Nodes, sub, filter);
            }

            foreach (var file in Directory.EnumerateFiles(dir)
                         .Where(IsApp)
                         .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                {
                    continue;
                }

                // evita doppioni (stesso nome già presente da un altro root)
                if (parent.Cast<TreeNode>().Any(n => n.Tag is string && string.Equals(n.Text, name, StringComparison.CurrentCultureIgnoreCase)))
                {
                    continue;
                }

                // niente icona ora (sarebbe lentissimo su centinaia di app):
                // l'icona viene caricata quando si espande la categoria
                var leaf = new TreeNode(name) { Tag = file, ImageKey = "__app", SelectedImageKey = "__app" };
                parent.Add(leaf);
            }
        }
        catch
        {
            // cartella non accessibile: ignora
        }
    }

    private static bool IsApp(string path)
    {
        var e = Path.GetExtension(path).ToLowerInvariant();
        return e is ".lnk" or ".url" or ".appref-ms" or ".exe";
    }

    private static TreeNode? FindFolder(TreeNodeCollection nodes, string name) =>
        nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag is null && string.Equals(n.Text, name, StringComparison.CurrentCultureIgnoreCase));

    private static TreeNode AddFolder(TreeNodeCollection parent, string name)
    {
        var node = new TreeNode(name) { ImageKey = "__folder", SelectedImageKey = "__folder" };
        parent.Add(node);
        return node;
    }

    // rimuove le cartelle rimaste senza applicazioni (es. dopo un filtro)
    private static void PruneEmpty(TreeNodeCollection nodes)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var n = nodes[i];
            if (n.Tag is null) // cartella
            {
                PruneEmpty(n.Nodes);
                if (n.Nodes.Count == 0)
                {
                    nodes.RemoveAt(i);
                }
            }
        }
    }

    private void OnNodeClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node.Tag is string path)
        {
            Launch(path);
            Close();
        }
        else
        {
            e.Node.Toggle();
        }
    }

    private void OnTreeKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _tree.SelectedNode?.Tag is string path)
        {
            Launch(path);
            Close();
        }
    }

    // --- avvio ------------------------------------------------------------

    private static void Launch(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // avvio fallito: ignora
        }
    }

    private static void LaunchArgs(string path, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, Arguments = args, UseShellExecute = true });
        }
        catch
        {
            // avvio fallito: ignora
        }
    }

    // --- icone generiche ---------------------------------------------------

    private static Bitmap FolderIcon()
    {
        var sfi = new SHFILEINFO();
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var res = SHGetFileInfo(dir, 0, ref sfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
        if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
        {
            try
            {
                using var ic = Icon.FromHandle(sfi.hIcon);
                var bmp = new Bitmap(ic.ToBitmap(), 16, 16);
                return bmp;
            }
            finally
            {
                DestroyIcon(sfi.hIcon);
            }
        }

        return GenericAppIcon(Color.FromArgb(240, 200, 90));
    }

    private static Bitmap GenericAppIcon() => GenericAppIcon(Color.FromArgb(120, 130, 150));

    private static Bitmap GenericAppIcon(Color c)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        using var b = new SolidBrush(c);
        g.FillRectangle(b, 2, 2, 12, 12);
        return bmp;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
