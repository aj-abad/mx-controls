namespace MxControls.Hidpp;

/// <summary>HID++ 2.0 feature IDs used by the MX Master 4.</summary>
public static class FeatureIds
{
    public const ushort Root = 0x0000;
    public const ushort FeatureSet = 0x0001;
    public const ushort FirmwareVersion = 0x0003;
    public const ushort DeviceName = 0x0005;
    public const ushort UnifiedBattery = 0x1004;
    public const ushort ChangeHost = 0x1814;
    public const ushort HostsInfo = 0x1815;
    public const ushort Haptic = 0x19B0;
    public const ushort ForceSensingButton = 0x19C0;
    public const ushort ReprogControlsV4 = 0x1B04;
    public const ushort WirelessDeviceStatus = 0x1D4B;
    public const ushort SmartShiftEnhanced = 0x2111;
    public const ushort HiResWheel = 0x2121;
    public const ushort ThumbWheel = 0x2150;
    public const ushort AdjustableDpi = 0x2201;
    public const ushort OnboardProfiles = 0x8100;

    private static readonly Dictionary<ushort, string> Names = new()
    {
        [Root] = "ROOT", [FeatureSet] = "FEATURE_SET", [FirmwareVersion] = "DEVICE_FW_VERSION",
        [DeviceName] = "DEVICE_NAME", [0x0007] = "DEVICE_FRIENDLY_NAME", [0x0020] = "CONFIG_CHANGE",
        [0x0021] = "CRYPTO_ID", [UnifiedBattery] = "UNIFIED_BATTERY", [0x1802] = "DEVICE_RESET",
        [0x1805] = "OOBSTATE", [ChangeHost] = "CHANGE_HOST", [HostsInfo] = "HOSTS_INFO",
        [Haptic] = "HAPTIC", [ForceSensingButton] = "FORCE_SENSING_BUTTON",
        [ReprogControlsV4] = "REPROG_CONTROLS_V4", [WirelessDeviceStatus] = "WIRELESS_DEVICE_STATUS",
        [0x2250] = "XY_STATS", [0x2251] = "WHEEL_STATS", [SmartShiftEnhanced] = "SMART_SHIFT_ENHANCED",
        [HiResWheel] = "HIRES_WHEEL", [ThumbWheel] = "THUMB_WHEEL", [AdjustableDpi] = "ADJUSTABLE_DPI",
        [OnboardProfiles] = "ONBOARD_PROFILES",
    };

    public static string NameOf(ushort id) => Names.TryGetValue(id, out var name) ? name : $"unknown:{id:X4}";
}
