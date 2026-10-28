using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Platform;

namespace MxControls.Remap;

/// <summary>
/// Makes mouse buttons behave as held keyboard keys: each mapped control is diverted, and changes in the
/// device's held-buttons reports become key-down and key-up events. Disposing releases held keys and
/// restores each control's original diversion.
/// </summary>
public sealed class HoldRemapper : IAsyncDisposable
{
    private readonly MxDevice _device;
    private readonly IKeyInjector _keys;
    private readonly IReadOnlyDictionary<ushort, string> _map;
    private readonly Dictionary<ushort, ControlReporting> _original = [];
    private readonly object _lock = new();
    private HashSet<ushort> _held = [];

    /// <summary>Raised for each mapped button change: control ID, key code, pressed.</summary>
    public event Action<ushort, string, bool>? Changed;
    public event Action<Exception>? Failed;

    private HoldRemapper(MxDevice device, IKeyInjector keys, IReadOnlyDictionary<ushort, string> map)
    {
        _device = device;
        _keys = keys;
        _map = map;
    }

    /// <param name="map">Control ID to <c>KeyboardEvent.code</c>.</param>
    public static async Task<HoldRemapper> StartAsync(MxDevice device, IKeyInjector keys, IReadOnlyDictionary<ushort, string> map)
    {
        if (map.Values.FirstOrDefault(code => !keys.Supports(code)) is { } unsupported)
            throw new ArgumentException($"This platform can't send the key '{unsupported}'.", nameof(map));

        var remapper = new HoldRemapper(device, keys, map);
        device.Channel.EventReceived += remapper.OnEvent;
        try
        {
            await remapper.DivertAsync();
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

    /// <summary>Diverts the mapped controls again, after the mouse reconnected and forgot its diversions.</summary>
    public async Task DivertAsync()
    {
        foreach (var cid in _map.Keys)
        {
            if (!_original.ContainsKey(cid))
                _original[cid] = await _device.Controls.GetReportingAsync(cid);
            await _device.Controls.SetDivertAsync(cid, divert: true, rawXY: _original[cid].RawXY);
        }
    }

    private void OnEvent(HidppEvent e)
    {
        if (_device.Controls.ParseHeld(e) is not { } now)
            return;
        lock (_lock)
        {
            try
            {
                foreach (var cid in now.Except(_held).Where(_map.ContainsKey))
                {
                    _keys.Down(_map[cid]);
                    Changed?.Invoke(cid, _map[cid], true);
                }
                foreach (var cid in _held.Except(now).Where(_map.ContainsKey))
                {
                    _keys.Up(_map[cid]);
                    Changed?.Invoke(cid, _map[cid], false);
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
            foreach (var cid in _held.Where(_map.ContainsKey))
            {
                try { _keys.Up(_map[cid]); } catch { }
            }
            _held.Clear();
        }
        foreach (var (cid, original) in _original)
        {
            try { await _device.Controls.SetDivertAsync(cid, original.Diverted, original.RawXY); } catch { }
        }
    }
}
