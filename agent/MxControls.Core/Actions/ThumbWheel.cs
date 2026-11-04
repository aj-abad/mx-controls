using System.Text.Json.Serialization;
using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// What the thumb wheel does. In <c>scroll</c> mode it scrolls sideways, at <see cref="Speed"/> percent of its own
/// speed; in <c>actions</c> mode each step runs <see cref="Left"/> or <see cref="Right"/>, such as a volume key.
/// </summary>
/// <param name="Speed">Percent, from <see cref="MinSpeed"/> to <see cref="MaxSpeed"/>; in actions mode, more steps per turn.</param>
/// <param name="Left">For <c>actions</c>: run once a step, as a tap even when it would hold keys or a button.</param>
public sealed record ThumbWheelSettings(
    string Mode = ThumbWheelSettings.ScrollMode, int Speed = 100, bool Inverted = false, ButtonAction? Left = null, ButtonAction? Right = null)
{
    public const string ScrollMode = "scroll";
    public const string ActionsMode = "actions";
    public const int MinSpeed = 25;
    public const int MaxSpeed = 300;

    public static readonly ThumbWheelSettings Default = new();

    /// <summary>The wheel's own behaviour, which leaves the mouse untouched.</summary>
    [JsonIgnore]
    public bool IsDefault => Mode == ScrollMode && Speed == 100 && !Inverted;

    /// <summary>Whether the agent has to take the wheel's turns; plain inverted scrolling is a setting on the mouse.</summary>
    [JsonIgnore]
    public bool Diverts => Mode == ActionsMode || Speed != 100;

    public string? Problem(IPlatform platform)
    {
        if (Speed is < MinSpeed or > MaxSpeed)
            return $"Speed must be from {MinSpeed} to {MaxSpeed}%.";
        return Mode switch
        {
            ScrollMode => null,
            ActionsMode when new[] { Left, Right }.Any(a => a?.Type is ActionTypes.Layer or ActionTypes.Profile) =>
                "The thumb wheel can't switch layers or profiles.",
            ActionsMode => (Left ?? ButtonAction.Nothing).Problem(platform) ?? (Right ?? ButtonAction.Nothing).Problem(platform),
            _ => $"Unknown thumb wheel mode '{Mode}'.",
        };
    }
}

/// <summary>
/// Turns diverted thumb wheel counts into sideways scrolling or action steps, positive to the right. Natively each
/// count of the wheel's native resolution scrolls one notch; that's the 100% speed here.
/// </summary>
internal sealed class ThumbWheelMapper(ThumbWheelSettings settings, int nativeResolution, int divertedResolution)
{
    public const int Notch = 120;

    // The diverted wheel reports finer counts than the native one; a bad report of either falls back to one-to-one.
    private readonly double _countsPerNotch =
        nativeResolution > 0 && divertedResolution > 0 ? (double)divertedResolution / nativeResolution : 1;
    private double _carry;

    /// <summary>Returns wheel units to scroll, 120 to a notch, in scroll mode, or whole steps in actions mode.</summary>
    public int Turn(ThumbWheelTurn turn)
    {
        // A new turn starts from rest, so half a step left over from the last one doesn't fire early.
        if (turn.Phase == 1)
            _carry = 0;
        double counts = settings.Inverted ? -turn.Rotation : turn.Rotation;
        double scale = settings.Speed / 100.0;
        _carry += settings.Mode == ThumbWheelSettings.ActionsMode
            ? counts * scale / _countsPerNotch
            : counts * scale * Notch / _countsPerNotch;
        int whole = (int)Math.Truncate(_carry);
        _carry -= whole;
        return whole;
    }
}

/// <summary>
/// Runs the thumb wheel's settings. Inverted native scrolling is set on the mouse; anything else diverts the
/// wheel and scrolls or runs actions from its turns. At the default it leaves the wheel as it found it, so the
/// wheel's reporting is written only when it has to change. Disposing puts back how the wheel reported before.
/// Callers serialize the async methods.
/// </summary>
public sealed class ThumbWheelEngine : IAsyncDisposable
{
    private readonly MxDevice _device;
    private readonly IPlatform _platform;
    private readonly ActionRunner _runner;
    private readonly object _gate = new();
    private ThumbWheelSettings _settings = ThumbWheelSettings.Default;
    private ThumbWheelSettings? _second;
    private ThumbWheelMapper? _mapper;
    private ThumbWheelMapper? _secondMapper;
    private bool _diverts;
    private bool _layerActive;
    // How the wheel reported before we first changed it, and what we've told it since; both null until then.
    private ThumbWheelState? _original;
    private (bool Divert, bool Invert)? _written;

    public event Action<Exception>? Failed;

    public ThumbWheelEngine(MxDevice device, IPlatform platform, MacroLibrary? macros = null)
    {
        _device = device;
        _platform = platform;
        _runner = new ActionRunner(platform, _gate, macros, error => Failed?.Invoke(error));
        device.Channel.EventReceived += OnEvent;
    }

    /// <summary>Whether another app, most likely Logi Options+, had diverted the wheel before we did; null if we never have.</summary>
    public bool? DivertedBefore => _original?.Diverted;

    /// <summary>Whether the profile's second layer is on, so <c>second</c> settings run instead.</summary>
    public bool LayerActive
    {
        set
        {
            lock (_gate)
                _layerActive = value;
        }
    }

    /// <summary>
    /// Runs these settings from now on, writing the wheel's reporting only if it changes. <paramref name="second"/>
    /// runs while the second layer is on, or null to keep <paramref name="settings"/> then too. With two that differ,
    /// the wheel stays diverted, so switching layers writes nothing to the mouse.
    /// </summary>
    public async Task UpdateAsync(ThumbWheelSettings settings, ThumbWheelSettings? second = null)
    {
        if (settings.Problem(_platform) is { } problem)
            throw new ArgumentException(problem, nameof(settings));
        if (second?.Problem(_platform) is { } secondProblem)
            throw new ArgumentException(secondProblem, nameof(second));
        if (second == settings)
            second = null;
        bool diverts = settings.Diverts || second is { } s && (s.Diverts || s.Inverted != settings.Inverted);
        if (!diverts && settings.IsDefault)
        {
            if (_original is { } original && _written is not null)
            {
                await _device.Wheels.SetThumbWheelAsync(original.Diverted, original.Inverted);
                _written = null;
            }
        }
        else
        {
            _original ??= await _device.Wheels.GetThumbWheelAsync();
            var wanted = (diverts, !diverts && settings.Inverted);
            if (_written != wanted)
            {
                await _device.Wheels.SetThumbWheelAsync(wanted.Item1, wanted.Item2);
                _written = wanted;
            }
        }
        lock (_gate)
        {
            _settings = settings;
            _second = second;
            _diverts = diverts;
            _mapper = _original is { } o ? new ThumbWheelMapper(settings, o.NativeResolution, o.DivertedResolution) : null;
            _secondMapper = _original is { } p && second is not null ? new ThumbWheelMapper(second, p.NativeResolution, p.DivertedResolution) : null;
        }
    }

    /// <summary>Sets the wheel's reporting again, after the mouse reconnected and may have forgotten it.</summary>
    public async Task ReapplyAsync()
    {
        if (_written is { } written)
            await _device.Wheels.SetThumbWheelAsync(written.Divert, written.Invert);
    }

    private void OnEvent(HidppEvent e)
    {
        if (_device.Wheels.ParseThumbWheel(e) is not { } turn)
            return;
        lock (_gate)
        {
            bool second = _layerActive && _second is not null;
            var (settings, mapper) = second ? (_second!, _secondMapper) : (_settings, _mapper);
            if (!_diverts || mapper is null)
                return;
            try
            {
                int amount = mapper.Turn(turn);
                if (amount == 0)
                    return;
                if (settings.Mode == ThumbWheelSettings.ScrollMode)
                {
                    _platform.Input.ScrollHorizontal(amount);
                    return;
                }
                var action = (amount > 0 ? settings.Right : settings.Left) ?? ButtonAction.Nothing;
                for (int i = 0; i < Math.Abs(amount); i++)
                    _runner.Run(action);
            }
            catch (Exception error)
            {
                Failed?.Invoke(error);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _device.Channel.EventReceived -= OnEvent;
        if (_original is { } original && _written is not null)
        {
            try { await _device.Wheels.SetThumbWheelAsync(original.Diverted, original.Inverted); } catch { }
        }
    }
}
