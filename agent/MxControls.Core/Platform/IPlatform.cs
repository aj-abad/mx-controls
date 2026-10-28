namespace MxControls.Platform;

/// <summary>One HID top-level collection.</summary>
public sealed record HidDeviceInfo(string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage, string Product);

public interface IHidBackend
{
    IReadOnlyList<HidDeviceInfo> Enumerate(ushort vendorId);

    /// <summary>A stream whose reads return whole input reports and whose writes send whole output reports.</summary>
    Stream Open(HidDeviceInfo device);
}

/// <summary>Sends key presses to the focused app. Keys are W3C <c>KeyboardEvent.code</c> names, such as "ShiftLeft".</summary>
public interface IKeyInjector
{
    bool Supports(string code);
    void Down(string code);
    void Up(string code);
}

public interface IPlatform
{
    IHidBackend Hid { get; }
    IKeyInjector Keys { get; }

    /// <summary>Whether Logi Options+ is running; it diverts buttons too and will act on them alongside us.</summary>
    bool IsOptionsPlusRunning();
}
