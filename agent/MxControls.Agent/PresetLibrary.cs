using System.Text.Json;
using MxControls.Actions;
using MxControls.Features;

namespace MxControls.Agent;

/// <summary>The gesture presets every install starts with, and moving gestures from before presets into them.</summary>
public static class PresetLibrary
{
    private static readonly string Mod = OperatingSystem.IsMacOS() ? "MetaLeft" : "ControlLeft";

    private static GestureStep Press(params string[] keys) => new(ActionTypes.Press, keys);
    private static GestureStep System(string name) => new(ActionTypes.System, System: name);
    private static GestureStep Mouse(string button) => new(ActionTypes.Click, Mouse: button);

    private static GestureStep Desktop(string arrow) =>
        OperatingSystem.IsMacOS() ? Press("ControlLeft", arrow) : Press("ControlLeft", "MetaLeft", arrow);

    /// <summary>In the order the app lists them, before the user's own.</summary>
    public static readonly IReadOnlyList<GesturePreset> BuiltIn =
    [
        new("desktops", "Desktops", System(SystemActions.TaskView), System(SystemActions.ShowDesktop), Desktop("ArrowLeft"), Desktop("ArrowRight")),
        new("browser", "Browser", Press(Mod, "KeyT"), Press(Mod, "KeyW"), Mouse(MouseButtons.Back), Mouse(MouseButtons.Forward)),
        new("media", "Media", Press("AudioVolumeUp") with { Repeat = true }, Press("AudioVolumeDown") with { Repeat = true },
            Press("MediaTrackPrevious"), Press("MediaTrackNext")),
    ];

    public static bool IsBuiltIn(string id) => BuiltIn.Any(p => p.Id == id);

    /// <summary>
    /// Settings with each gesture carried whole on a binding turned into a button that follows a preset. Gestures
    /// with the same moves share one; moves matching an existing preset follow it. Returns the same instance when
    /// there's nothing to move.
    /// </summary>
    public static AgentSettings Migrated(AgentSettings settings)
    {
        if (!settings.AllProfiles.Any(p => p.AllBindings.Any(b => b.IsGesture && b.Gesture is not null && b.Preset is null)))
            return settings;
        var presets = settings.AllPresets.ToList();
        var own = (settings.GesturePresets ?? []).ToList();
        var profiles = settings.AllProfiles.Select(profile => profile with
        {
            Bindings = profile.AllBindings.Select(binding =>
            {
                if (!binding.IsGesture || binding.Gesture is not { } gesture || binding.Preset is not null)
                    return binding;
                var moves = new GesturePreset("", "", gesture.Up, gesture.Down, gesture.Left, gesture.Right, gesture.SpacingMm);
                var preset = presets.FirstOrDefault(p => SameMoves(p, moves));
                if (preset is null)
                {
                    var button = ReprogControls.KnownButtons.TryGetValue(binding.Button, out var cid) ? ReprogControls.NameOf(cid) : binding.Button;
                    preset = moves with { Id = Profile.NewId(), Name = $"{button} button ({profile.Name})" };
                    presets.Add(preset);
                    own.Add(preset);
                }
                return new ButtonBinding(binding.Button, ButtonBinding.GestureMode, gesture.Click,
                    DoubleClick: gesture.DoubleClick, TripleClick: gesture.TripleClick, Preset: preset.Id);
            }).ToArray(),
        }).ToArray();
        return settings with { Profiles = profiles, GesturePresets = own };
    }

    /// <summary>Whether two presets do the same; names and icons aside.</summary>
    public static bool SameMoves(GesturePreset a, GesturePreset b) =>
        JsonSerializer.Serialize(a with { Id = "", Name = "", Icon = null }, AgentJson.Default.GesturePreset)
        == JsonSerializer.Serialize(b with { Id = "", Name = "", Icon = null }, AgentJson.Default.GesturePreset);
}
