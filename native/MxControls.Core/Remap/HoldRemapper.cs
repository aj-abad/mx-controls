using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Input;

namespace MxControls.Remap;

/// <summary>
/// Makes mouse buttons behave as held keyboard keys: each mapped control is diverted, and changes in the
/// device's held-buttons reports become key-down and key-up events. Disposing releases held keys and
/// restores each control's original diversion.
/// </summary>
public sealed class HoldRemapper : IAsyncDisposable
{
    private readonly MxDevice _device;
    private readonly IReadOnlyDictionary<ushort, ushort> _keys;
    private readonly Dictionary<ushort, ControlReporting> _original = [];
    private readonly object _lock = new();
    private HashSet<ushort> _held = [];

    /// <summary>Raised for each mapped button change: control ID, virtual key, pressed.</summary>
    public event Action<ushort, ushort, bool>? Changed;
    public event Action<Exception>? Failed;

    private HoldRemapper(MxDevice device, IReadOnlyDictionary<ushort, ushort> keys)
    {
        _device = device;
        _keys = keys;
    }

    /// <param name="keys">Control ID to virtual-key code.</param>
    public static async Task<HoldRemapper> StartAsync(MxDevice device, IReadOnlyDictionary<ushort, ushort> keys)
    {
        var remapper = new HoldRemapper(device, keys);
        device.Channel.EventReceived += remapper.OnEvent;
        try
        {
            foreach (var cid in keys.Keys)
            {
                remapper._original[cid] = await device.Controls.GetReportingAsync(cid);
                await device.Controls.SetDivertAsync(cid, divert: true, rawXY: remapper._original[cid].RawXY);
            }
            return remapper;
        }
        catch
        {
            await remapper.DisposeAsync();
            throw;
        }
    }

    /// <summary>Controls that were already diverted, most likely by Logi Options+, which will also act on them.</summary>
    public IEnumerable<ushort> SharedControls => _original.Where(o => o.Value.Diverted).Select(o => o.Key);

    private void OnEvent(HidppEvent e)
    {
        if (_device.Controls.ParseHeld(e) is not { } now)
            return;
        lock (_lock)
        {
            try
            {
                foreach (var cid in now.Except(_held).Where(_keys.ContainsKey))
                {
                    KeySender.Down(_keys[cid]);
                    Changed?.Invoke(cid, _keys[cid], true);
                }
                foreach (var cid in _held.Except(now).Where(_keys.ContainsKey))
                {
                    KeySender.Up(_keys[cid]);
                    Changed?.Invoke(cid, _keys[cid], false);
                }
            }
            catch (Exception error)
            {
                Failed?.Invoke(error);
            }
            _held = [.. now];
        }
    }

    public async ValueTask DisposeAsync()
    {
        _device.Channel.EventReceived -= OnEvent;
        lock (_lock)
        {
            foreach (var cid in _held.Where(_keys.ContainsKey))
            {
                try { KeySender.Up(_keys[cid]); } catch { }
            }
            _held.Clear();
        }
        foreach (var (cid, original) in _original)
        {
            try { await _device.Controls.SetDivertAsync(cid, original.Diverted, original.RawXY); } catch { }
        }
    }
}
