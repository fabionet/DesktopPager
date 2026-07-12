using System.Drawing;

namespace DesktopPager.Tray;

public sealed class DesktopIconPagerService : IDesktopIconPagerService
{
    private readonly int _spacingX;
    private readonly int _spacingY;
    private readonly int _hiddenOffsetMultiplier;
    private Dictionary<int, Point>? _baselineSlotPositions;
    private int? _lastAppliedPage;
    private int? _lastIconsPerPage;
    private int? _lastIconCount;

    public DesktopIconPagerService(int spacingX = 90, int spacingY = 90, int hiddenOffsetMultiplier = 6)
    {
        _spacingX = spacingX;
        _spacingY = spacingY;
        _hiddenOffsetMultiplier = hiddenOffsetMultiplier;
    }

    public int GetDesktopIconCount()
    {
        return NativeDesktopApi.GetDesktopIconCount();
    }

    public bool ApplyPageLayout(int currentPage, int iconsPerPage)
    {
        try
        {
            if (!EnsureBaselineLayout())
            {
                return false;
            }

            if (!NativeDesktopApi.TryGetDesktopClientRectangle(out var desktopArea))
            {
                return false;
            }

            var iconCount = GetDesktopIconCount();
            if (iconCount <= 0)
            {
                return true;
            }

            var targetPositions = IconPositioningEngine.CalculatePagePositions(
                iconCount,
                currentPage,
                iconsPerPage,
                desktopArea,
                _spacingX,
                _spacingY,
                _hiddenOffsetMultiplier,
                _baselineSlotPositions ?? new Dictionary<int, Point>());

            var success = true;
            foreach (var iconIndex in ComputeAffectedIndices(iconCount, currentPage, iconsPerPage))
            {
                if (targetPositions.TryGetValue(iconIndex, out var position))
                {
                    success &= NativeDesktopApi.TrySetDesktopIconPosition(iconIndex, position);
                }
            }

            if (success)
            {
                _lastAppliedPage = currentPage;
                _lastIconsPerPage = iconsPerPage;
                _lastIconCount = iconCount;
            }

            return success;
        }
        catch
        {
            return false;
        }
    }

    public bool EnsureBaselineLayout()
    {
        if (_baselineSlotPositions is not null)
        {
            return true;
        }

        if (!NativeDesktopApi.TryGetDesktopClientRectangle(out var desktopArea))
        {
            return false;
        }

        _baselineSlotPositions = BuildDeterministicSlots(desktopArea, _spacingX, _spacingY, maxSlots: 1000);
        return true;
    }

    public bool RestoreBaselineLayout()
    {
        if (!EnsureBaselineLayout() || _baselineSlotPositions is null)
        {
            return false;
        }

        var iconCount = GetDesktopIconCount();
        if (iconCount <= 0)
        {
            return true;
        }

        var success = true;
        foreach (var iconIndex in Enumerable.Range(0, iconCount))
        {
            var slotIndex = iconIndex % _baselineSlotPositions.Count;
            var position = _baselineSlotPositions[slotIndex];
            success &= NativeDesktopApi.TrySetDesktopIconPosition(iconIndex, position);
        }

        if (success)
        {
            _lastAppliedPage = 1;
            _lastIconsPerPage = null;
            _lastIconCount = iconCount;
        }

        return success;
    }

    private static Dictionary<int, Point> BuildDeterministicSlots(
        Rectangle area,
        int spacingX,
        int spacingY,
        int maxSlots)
    {
        var slots = new Dictionary<int, Point>(maxSlots);
        var maxColumns = Math.Max(1, area.Width / spacingX);

        for (var slot = 0; slot < maxSlots; slot++)
        {
            var col = slot % maxColumns;
            var row = slot / maxColumns;
            slots[slot] = new Point(area.Left + 8 + (col * spacingX), area.Top + 8 + (row * spacingY));
        }

        return slots;
    }

    private IEnumerable<int> ComputeAffectedIndices(int iconCount, int currentPage, int iconsPerPage)
    {
        var hasPreviousState =
            _lastAppliedPage.HasValue &&
            _lastIconsPerPage.HasValue &&
            _lastIconCount.HasValue &&
            _lastIconCount.Value == iconCount &&
            _lastIconsPerPage.Value == iconsPerPage;

        if (!hasPreviousState)
        {
            return Enumerable.Range(0, iconCount);
        }

        var previousRange = GetPageRange(_lastAppliedPage!.Value, iconsPerPage, iconCount);
        var currentRange = GetPageRange(currentPage, iconsPerPage, iconCount);
        return previousRange.Concat(currentRange).Distinct();
    }

    private static IEnumerable<int> GetPageRange(int page, int iconsPerPage, int iconCount)
    {
        var start = Math.Max(0, (page - 1) * iconsPerPage);
        var endExclusive = Math.Min(iconCount, start + iconsPerPage);
        return Enumerable.Range(start, Math.Max(0, endExclusive - start));
    }
}
