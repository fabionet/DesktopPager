namespace DesktopPager.Tray;

public interface IDesktopIconPagerService
{
    int GetDesktopIconCount();
    bool ScrollByPages(int pageDelta);
    bool ResetScroll();
    bool TryGetScrollStatus(out DesktopScrollStatus status);
}
