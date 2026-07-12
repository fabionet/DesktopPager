using System.Drawing;

namespace DesktopPager.Tray;

public static class IconPositioningEngine
{
    public static Dictionary<int, Point> CalculatePagePositions(
        int iconCount,
        int currentPage,
        int iconsPerPage,
        Rectangle area,
        int spacingX,
        int spacingY,
        int hiddenOffsetMultiplier,
        IReadOnlyDictionary<int, Point> baselineSlotPositions)
    {
        if (iconCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iconCount));
        }

        if (currentPage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPage));
        }

        if (iconsPerPage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iconsPerPage));
        }

        if (spacingX <= 0 || spacingY <= 0)
        {
            throw new ArgumentOutOfRangeException("Spacing values must be positive.");
        }

        baselineSlotPositions ??= new Dictionary<int, Point>();
        var result = new Dictionary<int, Point>(iconCount);

        var pageStartIndex = (currentPage - 1) * iconsPerPage;
        var pageEndExclusive = Math.Min(iconCount, pageStartIndex + iconsPerPage);

        var hiddenBaseX = area.Right + (spacingX * Math.Max(2, hiddenOffsetMultiplier));
        var hiddenY = Math.Max(area.Top + 8, 8);

        for (var iconIndex = 0; iconIndex < iconCount; iconIndex++)
        {
            var onCurrentPage = iconIndex >= pageStartIndex && iconIndex < pageEndExclusive;
            if (onCurrentPage)
            {
                var slotIndex = iconIndex - pageStartIndex;
                var visible = ResolveVisiblePosition(slotIndex, area, spacingX, spacingY, baselineSlotPositions);
                result[iconIndex] = ClampToSafeCoordinateRange(visible);
            }
            else
            {
                var hidden = new Point(hiddenBaseX + (iconIndex * 4), hiddenY);
                result[iconIndex] = ClampToSafeCoordinateRange(hidden);
            }
        }

        return result;
    }

    private static Point ResolveVisiblePosition(
        int slotIndex,
        Rectangle area,
        int spacingX,
        int spacingY,
        IReadOnlyDictionary<int, Point> baselineSlotPositions)
    {
        if (baselineSlotPositions.TryGetValue(slotIndex, out var baseline))
        {
            return baseline;
        }

        var maxColumns = Math.Max(1, area.Width / spacingX);
        var col = slotIndex % maxColumns;
        var row = slotIndex / maxColumns;
        return new Point(area.Left + 8 + (col * spacingX), area.Top + 8 + (row * spacingY));
    }

    private static Point ClampToSafeCoordinateRange(Point position)
    {
        const int min = -32760;
        const int max = 32760;
        var x = Math.Clamp(position.X, min, max);
        var y = Math.Clamp(position.Y, min, max);
        return new Point(x, y);
    }
}
