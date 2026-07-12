namespace DesktopPager.Tray;

public interface IDesktopIconPagerService
{
    int GetDesktopIconCount();
    bool ApplyPageLayout(int currentPage, int iconsPerPage);
    bool EnsureBaselineLayout();
    bool RestoreBaselineLayout();
}
