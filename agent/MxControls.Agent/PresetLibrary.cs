using System.Text.Json;
using MxControls.Actions;
using MxControls.Features;

namespace MxControls.Agent;

/// <summary>
/// The gesture presets every install starts with, and moving gestures and roses carried whole on buttons into the
/// libraries.
/// </summary>
public static class PresetLibrary
{
    private static readonly string Mod = OperatingSystem.IsMacOS() ? "MetaLeft" : "ControlLeft";

    private static ButtonAction Press(params string[] keys) => new(ActionTypes.Press, keys);
    private static ButtonAction System(string name) => new(ActionTypes.System, System: name);
    private static ButtonAction Mouse(string button) => new(ActionTypes.Click, Mouse: button);

    private static ButtonAction Desktop(string arrow) =>
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
    /// Settings with each gesture and rose carried whole on a binding turned into a button that follows a preset or
    /// shows a rose menu. Ones that do the same share one, and ones matching an existing preset or menu use it.
    /// Returns the same instance when there's nothing to move.
    /// </summary>
    public static AgentSettings Migrated(AgentSettings settings)
    {
        static bool Carried(ButtonBinding b) =>
            (b.IsGesture && b.Gesture is not null && b.Preset is null) || (b.IsRose && b.Rose is not null && b.Menu is null);
        if (!settings.AllProfiles.Any(p => p.AllBindings.Concat(p.SecondLayer.AllBindings).Any(Carried)))
            return settings;
        var presets = settings.AllPresets.ToList();
        var own = (settings.GesturePresets ?? []).ToList();
        var menus = (settings.RoseMenus ?? []).ToList();

        ButtonBinding Move(ButtonBinding binding, Profile profile, bool layer)
        {
            string Name()
            {
                var button = ReprogControls.KnownButtons.TryGetValue(binding.Button, out var cid) ? ReprogControls.NameOf(cid) : binding.Button;
                return $"{button} button ({profile.Name}{(layer ? ", second layer" : "")})";
            }
            if (binding is { IsGesture: true, Gesture: { } gesture, Preset: null })
            {
                var moves = new GesturePreset("", "", gesture.Up, gesture.Down, gesture.Left, gesture.Right);
                var preset = presets.FirstOrDefault(p => SameMoves(p, moves));
                if (preset is null)
                {
                    preset = moves with { Id = Profile.NewId(), Name = Name() };
                    presets.Add(preset);
                    own.Add(preset);
                }
                return new ButtonBinding(binding.Button, ButtonBinding.GestureMode, gesture.Click,
                    DoubleClick: gesture.DoubleClick, TripleClick: gesture.TripleClick, Preset: preset.Id);
            }
            if (binding is { IsRose: true, Rose: { } rose, Menu: null })
            {
                var menu = menus.FirstOrDefault(m => SameItems(m, rose));
                if (menu is null)
                {
                    menu = new RoseMenu(rose.Items, Profile.NewId(), Name());
                    menus.Add(menu);
                }
                return binding with { Rose = null, Menu = menu.Id };
            }
            return binding;
        }

        var profiles = settings.AllProfiles.Select(profile => profile with
        {
            Bindings = profile.AllBindings.Select(b => Move(b, profile, false)).ToArray(),
            Layer = profile.Layer is { } second ? second with { Bindings = second.AllBindings.Select(b => Move(b, profile, true)).ToArray() } : null,
        }).ToArray();
        return settings with { Profiles = profiles, GesturePresets = own, RoseMenus = menus };
    }

    /// <summary>Whether two rose menus show the same options; names and icons aside.</summary>
    public static bool SameItems(RoseMenu a, RoseMenu b) =>
        JsonSerializer.Serialize(new RoseMenu(a.Items), AgentJson.Default.RoseMenu)
        == JsonSerializer.Serialize(new RoseMenu(b.Items), AgentJson.Default.RoseMenu);

    /// <summary>Whether two presets do the same; names and icons aside.</summary>
    public static bool SameMoves(GesturePreset a, GesturePreset b) =>
        JsonSerializer.Serialize(a with { Id = "", Name = "", Icon = null }, AgentJson.Default.GesturePreset)
        == JsonSerializer.Serialize(b with { Id = "", Name = "", Icon = null }, AgentJson.Default.GesturePreset);
}
