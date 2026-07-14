using System.Runtime.InteropServices;

namespace DesktopPager.Tray;

/// <summary>
/// Ruota lo schermo principale tramite ChangeDisplaySettings
/// (equivalente alle hotkey dei driver video Intel).
/// </summary>
public static class ScreenRotationService
{
    public const int OrientationDefault = 0; // DMDO_DEFAULT
    public const int Orientation90 = 1;      // DMDO_90
    public const int Orientation180 = 2;     // DMDO_180
    public const int Orientation270 = 3;     // DMDO_270

    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const uint CdsTest = 0x00000002;

    public static bool Rotate(int orientation)
    {
        if (orientation is < 0 or > 3)
        {
            return false;
        }

        var dm = DevMode.Create();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref dm))
        {
            return false;
        }

        if (dm.dmDisplayOrientation == orientation)
        {
            return true;
        }

        // passando tra orizzontale e verticale vanno scambiate le dimensioni
        if (((dm.dmDisplayOrientation + orientation) & 1) == 1)
        {
            (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
        }

        dm.dmDisplayOrientation = orientation;
        return ChangeDisplaySettings(ref dm, 0) == DispChangeSuccessful;
    }

    /// <summary>Verifica se il driver supporta la rotazione, senza applicarla.</summary>
    public static bool IsRotationSupported()
    {
        var dm = DevMode.Create();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref dm))
        {
            return false;
        }

        (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
        dm.dmDisplayOrientation = dm.dmDisplayOrientation == Orientation90 ? OrientationDefault : Orientation90;
        return ChangeDisplaySettings(ref dm, CdsTest) == DispChangeSuccessful;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        private const int Cchdevicename = 32;
        private const int Cchformname = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Cchdevicename)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Cchformname)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;

        public static DevMode Create()
        {
            return new DevMode
            {
                dmDeviceName = string.Empty,
                dmFormName = string.Empty,
                dmSize = (ushort)Marshal.SizeOf<DevMode>()
            };
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettings(ref DevMode devMode, uint flags);
}
