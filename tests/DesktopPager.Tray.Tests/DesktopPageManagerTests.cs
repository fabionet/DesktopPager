namespace DesktopPager.Tray.Tests;

public sealed class DesktopPageManagerTests
{
    // Valori realistici presi da un desktop 1366x768 con due pagine di icone:
    // contenuto largo 2479px, pagina visibile 1366px, posizione massima 1113.
    private const int TwoPagesMaxRange = 2478;
    private const int PageSize = 1366;

    [Fact]
    public void RefreshState_Computes_TotalPages_From_ScrollRange()
    {
        var fake = new FakePagerService(TwoPagesMaxRange, PageSize) { StatusAvailable = true };
        var manager = new DesktopPageManager(fake);

        manager.RefreshState();

        Assert.Equal(2, manager.TotalPages);
        Assert.Equal(1, manager.CurrentPage);
    }

    [Fact]
    public void RefreshState_Reports_Three_Pages_For_Wider_Content()
    {
        var fake = new FakePagerService(maxRange: 4000, PageSize) { StatusAvailable = true };
        var manager = new DesktopPageManager(fake);

        manager.RefreshState();

        Assert.Equal(3, manager.TotalPages);
    }

    [Fact]
    public void RefreshState_Without_ScrollStatus_Defaults_To_First_Page()
    {
        // All'avvio LVS_NOSCROLL e' attivo e la scrollbar non esiste.
        var fake = new FakePagerService(TwoPagesMaxRange, PageSize) { StatusAvailable = false };
        var manager = new DesktopPageManager(fake);

        manager.RefreshState();

        Assert.Equal(1, manager.CurrentPage);
        Assert.Equal(1, manager.TotalPages);
    }

    [Fact]
    public void NextPage_Scrolls_Forward_One_Page()
    {
        var fake = new FakePagerService(TwoPagesMaxRange, PageSize);
        var manager = new DesktopPageManager(fake);

        var ok = manager.NextPage();

        Assert.True(ok);
        Assert.Equal(2, manager.CurrentPage);
        Assert.Equal(2, manager.TotalPages);
        Assert.True(fake.Position > 0);
    }

    [Fact]
    public void NextPage_At_Last_Page_Wraps_To_First_And_Restores_Style()
    {
        var fake = new FakePagerService(TwoPagesMaxRange, PageSize);
        var manager = new DesktopPageManager(fake);
        manager.NextPage(); // pagina 2 (ultima)

        var ok = manager.NextPage(); // wrap

        Assert.True(ok);
        Assert.Equal(1, manager.CurrentPage);
        Assert.Equal(0, fake.Position);
        Assert.Equal(1, fake.ResetCallCount);
        Assert.False(fake.StatusAvailable); // stile originale ripristinato
    }

    [Fact]
    public void PreviousPage_At_First_Page_Wraps_To_Last()
    {
        var fake = new FakePagerService(TwoPagesMaxRange, PageSize);
        var manager = new DesktopPageManager(fake);

        var ok = manager.PreviousPage();

        Assert.True(ok);
        Assert.Equal(2, manager.CurrentPage);
        Assert.Equal(fake.MaxPosition, fake.Position);
    }

    [Fact]
    public void PreviousPage_From_Second_Page_Returns_To_First_And_Restores_Style()
    {
        var fake = new FakePagerService(TwoPagesMaxRange, PageSize);
        var manager = new DesktopPageManager(fake);
        manager.NextPage(); // pagina 2

        var ok = manager.PreviousPage();

        Assert.True(ok);
        Assert.Equal(1, manager.CurrentPage);
        Assert.Equal(0, fake.Position);
        Assert.False(fake.StatusAvailable); // tornati all'origine: stile ripristinato
    }

    [Fact]
    public void GoToMainPage_Resets_Scroll()
    {
        var fake = new FakePagerService(TwoPagesMaxRange, PageSize);
        var manager = new DesktopPageManager(fake);
        manager.NextPage();

        var ok = manager.GoToMainPage();

        Assert.True(ok);
        Assert.Equal(1, manager.CurrentPage);
        Assert.Equal(0, fake.Position);
        Assert.Equal(1, fake.ResetCallCount);
    }

    [Fact]
    public void RefreshState_Reads_Icon_Count()
    {
        var fake = new FakePagerService(TwoPagesMaxRange, PageSize) { IconCount = 229 };
        var manager = new DesktopPageManager(fake);

        manager.RefreshState();

        Assert.Equal(229, manager.TotalIcons);
    }

    /// <summary>
    /// Simula la ListView del desktop: lo scroll e' limitato al range,
    /// ScrollByPages rimuove LVS_NOSCROLL (StatusAvailable = true) e
    /// ResetScroll lo ripristina (StatusAvailable = false).
    /// </summary>
    private sealed class FakePagerService : IDesktopIconPagerService
    {
        private readonly int _maxRange;
        private readonly int _pageSize;

        public FakePagerService(int maxRange, int pageSize)
        {
            _maxRange = maxRange;
            _pageSize = pageSize;
        }

        public int IconCount { get; set; }
        public int Position { get; private set; }
        public bool StatusAvailable { get; set; }
        public int ResetCallCount { get; private set; }

        public int MaxPosition => Math.Max(0, _maxRange - _pageSize + 1);

        public int GetDesktopIconCount()
        {
            return IconCount;
        }

        public bool ScrollByPages(int pageDelta)
        {
            StatusAvailable = true;
            Position = Math.Clamp(Position + (pageDelta * _pageSize), 0, MaxPosition);
            return true;
        }

        public bool ResetScroll()
        {
            Position = 0;
            StatusAvailable = false;
            ResetCallCount++;
            return true;
        }

        public bool TryGetScrollStatus(out DesktopScrollStatus status)
        {
            status = new DesktopScrollStatus(Position, _maxRange, _pageSize);
            return StatusAvailable;
        }
    }
}
