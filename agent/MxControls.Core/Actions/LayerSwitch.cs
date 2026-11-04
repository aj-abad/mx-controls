namespace MxControls.Actions;

/// <summary>
/// A profile's two layers of bindings, by control ID, and which one runs. The second layer is on while a button
/// holding it is down, or once a toggle has turned it on. A control the second layer leaves unbound runs its main
/// binding. Callers serialize access.
/// </summary>
internal sealed class LayerSwitch
{
    private readonly HashSet<ushort> _holds = [];
    private HashSet<ushort> _controls = [];

    public IReadOnlyDictionary<ushort, ButtonBinding> Main { get; private set; } = new Dictionary<ushort, ButtonBinding>();
    public IReadOnlyDictionary<ushort, ButtonBinding> Second { get; private set; } = new Dictionary<ushort, ButtonBinding>();

    public bool Toggled { get; private set; }

    public bool Active => Toggled || _holds.Count > 0;

    /// <summary>Every control bound in either layer, which all stay diverted so switching layers writes nothing.</summary>
    public IReadOnlySet<ushort> Controls => _controls;

    /// <summary>What the control runs now, or null where neither layer binds it.</summary>
    public ButtonBinding? this[ushort cid] =>
        Active && Second.TryGetValue(cid, out var second) ? second : Main.GetValueOrDefault(cid);

    public void Set(IReadOnlyDictionary<ushort, ButtonBinding> main, IReadOnlyDictionary<ushort, ButtonBinding> second)
    {
        Main = main;
        Second = second;
        _controls = [.. main.Keys, .. second.Keys];
    }

    /// <summary>A button holding the layer went down. Returns whether the layer turned on.</summary>
    public bool Hold(ushort cid) => Changes(() => _holds.Add(cid));

    /// <summary>A button came up; only one holding the layer changes anything. Returns whether the layer changed.</summary>
    public bool Release(ushort cid) => Changes(() => _holds.Remove(cid));

    /// <summary>Turns the layer on, or off. Returns whether that changed which layer runs.</summary>
    public bool Toggle() => Changes(() => Toggled = !Toggled);

    /// <summary>Back to the main layer, for a profile that has just started running. Returns whether it changed.</summary>
    public bool Reset() => Changes(() =>
    {
        Toggled = false;
        _holds.Clear();
    });

    /// <summary>Lets go of every hold, after presses were cut off, keeping a toggle. Returns whether the layer changed.</summary>
    public bool ReleaseAll() => Changes(_holds.Clear);

    private bool Changes(Action change)
    {
        bool before = Active;
        change();
        return Active != before;
    }
}
