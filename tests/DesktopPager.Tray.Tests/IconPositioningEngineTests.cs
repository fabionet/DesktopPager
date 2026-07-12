using System.Drawing;
using DesktopPager.Tray;

namespace DesktopPager.Tray.Tests;

public sealed class IconPositioningEngineTests
{
    [Fact]
    public void CalculatePagePositions_PageOne_UsesSlotBaseline_ForVisibleIcons()
    {
        var area = new Rectangle(0, 0, 600, 400);
        var baseline = new Dictionary<int, Point>
        {
            [0] = new(10, 10),
            [1] = new(110, 10),
            [2] = new(210, 10),
            [3] = new(310, 10)
        };

        var positions = IconPositioningEngine.CalculatePagePositions(
            iconCount: 6,
            currentPage: 1,
            iconsPerPage: 4,
            area: area,
            spacingX: 100,
            spacingY: 100,
            hiddenOffsetMultiplier: 6,
            baselineSlotPositions: baseline);

        Assert.Equal(new Point(10, 10), positions[0]);
        Assert.Equal(new Point(110, 10), positions[1]);
        Assert.Equal(new Point(210, 10), positions[2]);
        Assert.Equal(new Point(310, 10), positions[3]);
        Assert.True(positions[4].X > area.Right);
        Assert.True(positions[5].X > area.Right);
    }

    [Fact]
    public void CalculatePagePositions_PageTwo_ReusesSlotPositions_ForNextIconBlock()
    {
        var area = new Rectangle(0, 0, 600, 400);
        var baseline = new Dictionary<int, Point>
        {
            [0] = new(15, 15),
            [1] = new(115, 15),
            [2] = new(215, 15),
            [3] = new(315, 15)
        };

        var positions = IconPositioningEngine.CalculatePagePositions(
            iconCount: 8,
            currentPage: 2,
            iconsPerPage: 4,
            area: area,
            spacingX: 100,
            spacingY: 100,
            hiddenOffsetMultiplier: 6,
            baselineSlotPositions: baseline);

        Assert.Equal(new Point(15, 15), positions[4]);
        Assert.Equal(new Point(115, 15), positions[5]);
        Assert.Equal(new Point(215, 15), positions[6]);
        Assert.Equal(new Point(315, 15), positions[7]);
        Assert.True(positions[0].X > area.Right);
        Assert.True(positions[1].X > area.Right);
    }

    [Fact]
    public void CalculatePagePositions_FallsBackToGrid_WhenBaselineSlotMissing()
    {
        var area = new Rectangle(0, 0, 500, 300);
        var baseline = new Dictionary<int, Point>
        {
            [0] = new(20, 20)
        };

        var positions = IconPositioningEngine.CalculatePagePositions(
            iconCount: 3,
            currentPage: 1,
            iconsPerPage: 3,
            area: area,
            spacingX: 90,
            spacingY: 90,
            hiddenOffsetMultiplier: 6,
            baselineSlotPositions: baseline);

        Assert.Equal(new Point(20, 20), positions[0]);
        Assert.Equal(new Point(98, 8), positions[1]);
        Assert.Equal(new Point(188, 8), positions[2]);
    }
}
