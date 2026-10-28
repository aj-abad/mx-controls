using System.Text;
using MxControls.Hidpp;

namespace MxControls.Features;

public sealed record FirmwareEntity(int Type, string Prefix, int Number, int Revision, int Build)
{
    public string TypeName => Type switch { 0 => "Main", 1 => "Bootloader", 2 => "Hardware", 3 => "Touchpad", 4 => "Optical sensor", _ => $"Type {Type}" };
    public override string ToString() => $"{Prefix} {Number:X2}.{Revision:X2} build {Build:X4}";
}

public readonly record struct BatteryState(int Percent, bool Charging, bool ExternalPower);

/// <summary>DEVICE_NAME (0x0005), DEVICE_FW_VERSION (0x0003) and UNIFIED_BATTERY (0x1004).</summary>
public sealed class DeviceInfo(MxDevice device)
{
    public async Task<string> GetNameAsync()
    {
        int length = (await device.CallAsync(FeatureIds.DeviceName, 0))[0];
        var name = new StringBuilder();
        while (name.Length < length)
        {
            var chunk = await device.CallAsync(FeatureIds.DeviceName, 1, (byte)name.Length);
            name.Append(Encoding.ASCII.GetString(chunk, 0, Math.Min(chunk.Length, length - name.Length)));
        }
        return name.ToString();
    }

    public async Task<IReadOnlyList<FirmwareEntity>> GetFirmwareAsync()
    {
        int count = (await device.CallAsync(FeatureIds.FirmwareVersion, 0))[0];
        var entities = new List<FirmwareEntity>(count);
        for (int i = 0; i < count; i++)
        {
            var r = await device.CallAsync(FeatureIds.FirmwareVersion, 1, (byte)i);
            entities.Add(new FirmwareEntity(r[0], Encoding.ASCII.GetString(r, 1, 3).TrimEnd('\0', ' '), r[4], r[5], (r[6] << 8) | r[7]));
        }
        return entities;
    }

    public async Task<BatteryState> GetBatteryAsync()
    {
        var r = await device.CallAsync(FeatureIds.UnifiedBattery, 1);
        // Charging status: 0 discharging, 1 charging, 2 slow charging, 3 full, 4 error.
        return new BatteryState(r[0], r[2] is 1 or 2, r[3] != 0);
    }
}
