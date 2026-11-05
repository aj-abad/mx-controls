using MxControls.Features;

namespace MxControls.Tests;

public class BatteryTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void ReadsChargingFromTheStatus(byte status, bool charging)
    {
        var battery = DeviceInfo.ParseBatteryStatus([65, 4, status, 1]);
        Assert.Equal(new BatteryState(65, charging, true), battery);
    }

    [Fact]
    public void ReadsExternalPower()
    {
        Assert.False(DeviceInfo.ParseBatteryStatus([40, 2, 0, 0]).ExternalPower);
    }
}
