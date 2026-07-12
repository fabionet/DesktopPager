using System.Drawing;

namespace DesktopPager.Tray;

public sealed class DesktopIconPagerService : IDesktopIconPagerService
{
    private readonly int _spacingX;
    private readonly int _spacingY;
    private readonly int _hiddenOffsetMultiplier;
    private Dictionary<int, Point>? _baselineIconPositions;
    private Dictionary<int, Point>? _baselineSlotPositions;

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
        foreach (var (iconIndex, position) in targetPositions)
        {
            success &= NativeDesktopApi.TrySetDesktopIconPosition(iconIndex, position);
        }

        return success;
    }

    public bool EnsureBaselineLayout()
    {
        if (_baselineIconPositions is not null && _baselineSlotPositions is not null)
        {
            return true;
        }

        if (!NativeDesktopApi.TryGetDesktopIconPositions(out var currentPositions))
        {
            return false;
        }

        _baselineIconPositions = currentPositions;
        _baselineSlotPositions = currentPositions
            .OrderBy(x => x.Key)
            .Take(200)
            .Select((kvp, slot) => new KeyValuePair<int, Point>(slot, kvp.Value))
            .ToDictionary(x => x.Key, x => x.Value);

        return true;
    }

    public bool RestoreBaselineLayout()
    {
        if (_baselineIconPositions is null)
        {
            return false;
        }

        var success = true;
        foreach (var (iconIndex, position) in _baselineIconPositions)
        {
            success &= NativeDesktopApi.TrySetDesktopIconPosition(iconIndex, position);
        }

        return success;
    }
}
