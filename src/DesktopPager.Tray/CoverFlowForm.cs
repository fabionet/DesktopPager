using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DesktopPager.Tray;

/// <summary>
/// Vista 3D "cover flow" delle anteprime dei file: prospettiva simulata in
/// GDI+ puro (niente GPU: adatta anche a macchine datate). Frecce o rotella
/// per scorrere, Invio/doppio clic per aprire, Esc per chiudere.
/// </summary>
public sealed class CoverFlowForm : Form
{
    private const int ThumbSize = 256;
    private const int SideCount = 4;          // copertine visibili per lato
    private const int MaxFiles = 300;

    private readonly List<string> _files = new();
    private readonly Dictionary<int, Bitmap?> _thumbs = new();
    private int _index;

    public CoverFlowForm(string folder)
    {
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(12, 12, 16);
        KeyPreview = true;
        DoubleBuffered = true;

        try
        {
            _files.AddRange(Directory.EnumerateFiles(folder)
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .Take(MaxFiles));
        }
        catch
        {
            // cartella non leggibile: resta vuota
        }

        KeyDown += OnKey;
        MouseWheel += (_, e) => Move(e.Delta > 0 ? -1 : +1);
        MouseDoubleClick += (_, _) => OpenCurrent();
        MouseClick += OnClick;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        TopMost = true;
        Activate();
        BringToFront();
        Focus();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape: Close(); break;
            case Keys.Left: Move(-1); break;
            case Keys.Right: Move(+1); break;
            case Keys.Home: _index = 0; Invalidate(); break;
            case Keys.End: _index = Math.Max(0, _files.Count - 1); Invalidate(); break;
            case Keys.Enter: OpenCurrent(); break;
        }
    }

    private void OnClick(object? sender, MouseEventArgs e)
    {
        // clic a destra/sinistra del centro per scorrere
        var cx = ClientSize.Width / 2;
        if (Math.Abs(e.X - cx) > ThumbSize / 2)
        {
            Move(e.X > cx ? +1 : -1);
        }
    }

    private void Move(int delta)
    {
        var next = Math.Clamp(_index + delta, 0, Math.Max(0, _files.Count - 1));
        if (next != _index)
        {
            _index = next;
            Invalidate();
        }
    }

    private void OpenCurrent()
    {
        if (_index < 0 || _index >= _files.Count)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = _files[_index], UseShellExecute = true });
        }
        catch
        {
            // apertura fallita: ignora
        }
    }

    private Bitmap? Thumb(int i)
    {
        if (!_thumbs.TryGetValue(i, out var bmp))
        {
            bmp = i >= 0 && i < _files.Count ? ThumbnailProvider.GetThumbnail(_files[i], ThumbSize) : null;
            _thumbs[i] = bmp;

            // tieni in cache solo un intorno dell'indice corrente
            if (_thumbs.Count > 40)
            {
                foreach (var k in _thumbs.Keys.Where(k => Math.Abs(k - _index) > 12).ToList())
                {
                    _thumbs[k]?.Dispose();
                    _thumbs.Remove(k);
                }
            }
        }

        return bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;

        var w = ClientSize.Width;
        var h = ClientSize.Height;

        if (_files.Count == 0)
        {
            TextRenderer.DrawText(g, "Nessun file nella cartella", new Font("Segoe UI", 16f), new Point(40, 40), Color.White);
            return;
        }

        var cx = w / 2;
        var cy = h / 2 - 30;
        var spacing = ThumbSize / 2 + 40;

        // copertine laterali dalla piu' lontana alla piu' vicina
        for (var d = SideCount; d >= 1; d--)
        {
            DrawCover(g, _index - d, cx - (ThumbSize / 2 + 60) - (d - 1) * spacing / 2, cy, d, true);
            DrawCover(g, _index + d, cx + (ThumbSize / 2 + 60) + (d - 1) * spacing / 2, cy, d, false);
        }

        // copertina centrale in primo piano
        DrawCover(g, _index, cx, cy, 0, false);

        // nome file e contatore
        var name = Path.GetFileName(_files[_index]);
        TextRenderer.DrawText(g, name, new Font("Segoe UI", 14f, FontStyle.Bold),
            new Rectangle(0, cy + ThumbSize / 2 + 30, w, 34), Color.White,
            TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, $"{_index + 1} di {_files.Count}   (frecce = scorri, Invio = apri, Esc = chiudi)",
            new Font("Segoe UI", 10f),
            new Rectangle(0, cy + ThumbSize / 2 + 66, w, 26), Color.Gainsboro,
            TextFormatFlags.HorizontalCenter);
    }

    private void DrawCover(Graphics g, int i, int x, int cy, int depth, bool leftSide)
    {
        if (i < 0 || i >= _files.Count)
        {
            return;
        }

        var bmp = Thumb(i);
        if (bmp is null)
        {
            return;
        }

        var scale = depth == 0 ? 1.0f : Math.Max(0.35f, 0.72f - (depth - 1) * 0.12f);
        var sw = (int)(ThumbSize * scale);
        var sh = (int)(ThumbSize * scale);
        var shear = depth == 0 ? 0 : (int)(sh * 0.12f); // inclinazione prospettica

        Point[] dest;
        if (depth == 0)
        {
            dest = new[]
            {
                new Point(x - sw / 2, cy - sh / 2),
                new Point(x + sw / 2, cy - sh / 2),
                new Point(x - sw / 2, cy + sh / 2)
            };
        }
        else if (leftSide)
        {
            // il bordo esterno (sinistro) e' piu' "lontano": piu' corto
            dest = new[]
            {
                new Point(x - sw / 2, cy - sh / 2 + shear),
                new Point(x + sw / 2, cy - sh / 2),
                new Point(x - sw / 2, cy + sh / 2 - shear)
            };
        }
        else
        {
            dest = new[]
            {
                new Point(x - sw / 2, cy - sh / 2),
                new Point(x + sw / 2, cy - sh / 2 + shear),
                new Point(x - sw / 2, cy + sh / 2)
            };
        }

        // le copertine lontane sono attenuate
        if (depth > 0)
        {
            using var dark = new Bitmap(bmp.Width, bmp.Height);
            using (var dg = Graphics.FromImage(dark))
            {
                dg.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                using var veil = new SolidBrush(Color.FromArgb(60 + depth * 35, 12, 12, 16));
                dg.FillRectangle(veil, 0, 0, bmp.Width, bmp.Height);
            }
            g.DrawImage(dark, dest);
        }
        else
        {
            g.DrawImage(bmp, dest);
            using var pen = new Pen(ThemeService.Accent, 3f);
            g.DrawRectangle(pen, x - sw / 2, cy - sh / 2, sw, sh);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var b in _thumbs.Values)
        {
            b?.Dispose();
        }
        base.OnFormClosed(e);
    }
}
