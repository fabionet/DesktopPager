namespace DesktopPager.Tray;

public sealed class DesktopPageManager
{
    private readonly int _maxPages;
    private readonly int _iconsPerPage;
    private readonly IDesktopIconPagerService _pagerService;

    public DesktopPageManager(int maxPages, int iconsPerPage, IDesktopIconPagerService pagerService)
    {
        if (maxPages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages));
        }

        if (iconsPerPage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iconsPerPage));
        }

        _maxPages = maxPages;
        _iconsPerPage = iconsPerPage;
        _pagerService = pagerService ?? throw new ArgumentNullException(nameof(pagerService));

        CurrentPage = 1;
        TotalPages = 1;
    }

    public int CurrentPage { get; private set; }
    public int TotalPages { get; private set; }
    public int TotalIcons { get; private set; }

    public void RefreshState()
    {
        TotalIcons = Math.Max(0, _pagerService.GetDesktopIconCount());
        var computedPages = TotalIcons == 0
            ? 1
            : (int)Math.Ceiling(TotalIcons / (double)_iconsPerPage);

        TotalPages = Math.Clamp(computedPages, 1, _maxPages);
        CurrentPage = Math.Clamp(CurrentPage, 1, TotalPages);
    }

    public bool NextPage()
    {
        RefreshState();
        var targetPage = CurrentPage >= TotalPages ? 1 : CurrentPage + 1;
        return ApplyPage(targetPage);
    }

    public bool PreviousPage()
    {
        RefreshState();
        var targetPage = CurrentPage <= 1 ? TotalPages : CurrentPage - 1;
        return ApplyPage(targetPage);
    }

    public bool GoToMainPage()
    {
        RefreshState();
        return ApplyPage(1);
    }

    public bool ApplyPage(int page)
    {
        RefreshState();
        if (page < 1 || page > TotalPages)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, $"Page must be between 1 and {TotalPages}.");
        }

        if (!_pagerService.ApplyPageLayout(page, _iconsPerPage))
        {
            return false;
        }

        CurrentPage = page;
        return true;
    }

    public bool EnsureBaselineLayout()
    {
        return _pagerService.EnsureBaselineLayout();
    }

    public bool RestoreBaselineLayout()
    {
        return _pagerService.RestoreBaselineLayout();
    }
}
