namespace DesktopPager.Tray;

/// <summary>
/// Sfoglia le pagine del desktop facendo scorrere la vista della ListView
/// (LVM_SCROLL). A differenza del vecchio approccio a riposizionamento,
/// non muove mai le icone: il layout dell'utente resta intatto e il
/// meccanismo funziona anche con "disposizione automatica" attiva.
/// </summary>
public sealed class DesktopIconPagerService : IDesktopIconPagerService
{
    public int GetDesktopIconCount()
    {
        return NativeDesktopApi.GetDesktopIconCount();
    }

    public bool ScrollByPages(int pageDelta)
    {
        return NativeDesktopApi.TryScrollDesktopByPages(pageDelta);
    }

    public bool ResetScroll()
    {
        return NativeDesktopApi.TryResetDesktopScroll();
    }

    public bool TryGetScrollStatus(out DesktopScrollStatus status)
    {
        return NativeDesktopApi.TryGetDesktopScrollStatus(out status);
    }
}
