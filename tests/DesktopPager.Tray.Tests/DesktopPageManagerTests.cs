using DesktopPager.Tray;

namespace DesktopPager.Tray.Tests;

public sealed class DesktopPageManagerTests
{
    [Fact]
    public void RefreshState_Computes_TotalPages_From_IconCount()
    {
        var fakeService = new FakePagerService(iconCount: 260);
        var manager = new DesktopPageManager(maxPages: 10, iconsPerPage: 100, fakeService);

        manager.RefreshState();

        Assert.Equal(260, manager.TotalIcons);
        Assert.Equal(3, manager.TotalPages);
    }

    [Fact]
    public void RefreshState_Clamps_TotalPages_To_MaxPages()
    {
        var fakeService = new FakePagerService(iconCount: 2000);
        var manager = new DesktopPageManager(maxPages: 5, iconsPerPage: 100, fakeService);

        manager.RefreshState();

        Assert.Equal(5, manager.TotalPages);
    }

    [Fact]
    public void NextPage_Wraps_From_Last_To_First()
    {
        var fakeService = new FakePagerService(iconCount: 250);
        var manager = new DesktopPageManager(maxPages: 10, iconsPerPage: 100, fakeService);
        manager.ApplyPage(3);

        manager.NextPage();

        Assert.Equal(1, manager.CurrentPage);
        Assert.Equal((1, 100), fakeService.LastApplyCall);
    }

    [Fact]
    public void PreviousPage_Wraps_From_First_To_Last()
    {
        var fakeService = new FakePagerService(iconCount: 350);
        var manager = new DesktopPageManager(maxPages: 10, iconsPerPage: 100, fakeService);
        manager.ApplyPage(1);

        manager.PreviousPage();

        Assert.Equal(4, manager.CurrentPage);
        Assert.Equal((4, 100), fakeService.LastApplyCall);
    }

    [Fact]
    public void ApplyPage_Throws_When_Page_Out_Of_Range()
    {
        var fakeService = new FakePagerService(iconCount: 120);
        var manager = new DesktopPageManager(maxPages: 10, iconsPerPage: 100, fakeService);

        Assert.Throws<ArgumentOutOfRangeException>(() => manager.ApplyPage(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.ApplyPage(3));
    }

    [Fact]
    public void EnsureBaselineLayout_Passthroughs_Service_Result()
    {
        var fakeService = new FakePagerService(iconCount: 120)
        {
            EnsureBaselineResult = true
        };
        var manager = new DesktopPageManager(maxPages: 10, iconsPerPage: 100, fakeService);

        var result = manager.EnsureBaselineLayout();

        Assert.True(result);
        Assert.Equal(1, fakeService.EnsureBaselineCallCount);
    }

    [Fact]
    public void RestoreBaselineLayout_Passthroughs_Service_Result()
    {
        var fakeService = new FakePagerService(iconCount: 120)
        {
            RestoreBaselineResult = true
        };
        var manager = new DesktopPageManager(maxPages: 10, iconsPerPage: 100, fakeService);

        var result = manager.RestoreBaselineLayout();

        Assert.True(result);
        Assert.Equal(1, fakeService.RestoreBaselineCallCount);
    }

    private sealed class FakePagerService : IDesktopIconPagerService
    {
        private readonly int _iconCount;

        public FakePagerService(int iconCount)
        {
            _iconCount = iconCount;
        }

        public (int page, int iconsPerPage)? LastApplyCall { get; private set; }
        public bool EnsureBaselineResult { get; set; } = true;
        public bool RestoreBaselineResult { get; set; } = true;
        public int EnsureBaselineCallCount { get; private set; }
        public int RestoreBaselineCallCount { get; private set; }

        public int GetDesktopIconCount()
        {
            return _iconCount;
        }

        public bool ApplyPageLayout(int currentPage, int iconsPerPage)
        {
            LastApplyCall = (currentPage, iconsPerPage);
            return true;
        }

        public bool EnsureBaselineLayout()
        {
            EnsureBaselineCallCount++;
            return EnsureBaselineResult;
        }

        public bool RestoreBaselineLayout()
        {
            RestoreBaselineCallCount++;
            return RestoreBaselineResult;
        }
    }
}
