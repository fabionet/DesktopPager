namespace DesktopPager.Tray;

public sealed class DesktopPageManager
{
    // Un delta molto grande viene comunque limitato dal range della ListView:
    // e' il modo piu' semplice per "saltare all'ultima pagina".
    private const int JumpToEndPages = 100;

    private readonly IDesktopIconPagerService _pagerService;

    public DesktopPageManager(IDesktopIconPagerService pagerService)
    {
        _pagerService = pagerService ?? throw new ArgumentNullException(nameof(pagerService));
    }

    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public int TotalIcons { get; private set; }

    public void RefreshState()
    {
        TotalIcons = Math.Max(0, _pagerService.GetDesktopIconCount());

        if (_pagerService.TryGetScrollStatus(out var status) && status.PageSize > 0)
        {
            TotalPages = Math.Max(1, (int)Math.Ceiling((status.MaxRange + 1) / (double)status.PageSize));
            var maxPosition = Math.Max(0, status.MaxRange - status.PageSize + 1);
            CurrentPage = maxPosition == 0 || TotalPages == 1
                ? 1
                : 1 + (int)Math.Round(status.Position / (double)maxPosition * (TotalPages - 1));
        }
        else
        {
            // Alla prima pagina lo stile originale del desktop (LVS_NOSCROLL)
            // e' attivo e la scrollbar non esiste: manteniamo l'ultimo stato noto.
            CurrentPage = Math.Clamp(CurrentPage, 1, TotalPages);
        }
    }

    public bool NextPage()
    {
        var hadStatus = _pagerService.TryGetScrollStatus(out var before);

        var ok = _pagerService.ScrollByPages(1);
        RefreshState();

        // Se eravamo gia' a fine corsa la posizione non cambia: wrap alla prima.
        if (ok && hadStatus
            && _pagerService.TryGetScrollStatus(out var after)
            && after.Position == before.Position)
        {
            ok = ResetToFirstPage();
        }

        return ok;
    }

    public bool PreviousPage()
    {
        RefreshState();

        bool ok;
        if (!_pagerService.TryGetScrollStatus(out var status) || status.Position <= 0)
        {
            // prima pagina: wrap all'ultima
            ok = _pagerService.ScrollByPages(JumpToEndPages);
        }
        else
        {
            ok = _pagerService.ScrollByPages(-1);
        }

        RefreshState();

        // Se siamo tornati all'origine, ripristina lo stile originale del desktop.
        if (ok && _pagerService.TryGetScrollStatus(out var after) && after.Position <= 0)
        {
            ok = ResetToFirstPage();
        }

        return ok;
    }

    public bool GoToMainPage()
    {
        return ResetToFirstPage();
    }

    private bool ResetToFirstPage()
    {
        var ok = _pagerService.ResetScroll();
        if (ok)
        {
            CurrentPage = 1;
        }

        return ok;
    }
}
