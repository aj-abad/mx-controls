using System.Text.Json;
using MxControls.Actions;
using MxControls.Agent;

namespace MxControls.Tests;

public class BindingTests
{
    private static readonly FakePlatform Platform = new();

    private static ButtonAction Press(params string[] keys) => new(ActionTypes.Press, keys);

    [Fact]
    public void AcceptsAnActionAndAGesture()
    {
        Assert.Null(new ButtonBinding("middle", ButtonBinding.ActionMode, Press("ControlLeft", "KeyC")).Problem(Platform));
        var gesture = new GestureActions(Press("ControlLeft", "KeyC"), Press("ControlLeft", "KeyV"), Press("Delete"),
            ButtonAction.Nothing, new ButtonAction(ActionTypes.System, System: SystemActions.TaskView));
        Assert.Null(new ButtonBinding("gesture", ButtonBinding.GestureMode, Gesture: gesture).Problem(Platform));
    }

    [Fact]
    public void AcceptsAKnownMouseButtonOnly()
    {
        var back = new ButtonAction(ActionTypes.Click, Mouse: MouseButtons.Back);
        Assert.Null(new ButtonBinding("gesture", ButtonBinding.ActionMode, back).Problem(Platform));
        Assert.Contains("Unknown mouse button 'left'",
            new ButtonBinding("gesture", ButtonBinding.ActionMode, back with { Mouse = "left" }).Problem(Platform));
        Assert.Contains("Unknown mouse button",
            new ButtonBinding("gesture", ButtonBinding.ActionMode, back with { Mouse = null }).Problem(Platform));
    }

    [Theory]
    [InlineData("thumb", "action", "press", "KeyC", null, "Unknown button")]
    [InlineData("middle", "action", "press", "F99", null, "can't send the key 'F99'")]
    [InlineData("middle", "action", "press", null, null, "at least one key")]
    [InlineData("middle", "action", "system", null, "launchRocket", "Unknown system action")]
    [InlineData("middle", "swipe", "press", "KeyC", null, "Unknown mode")]
    public void ExplainsWhatIsWrong(string button, string mode, string type, string? key, string? system, string expected)
    {
        var action = new ButtonAction(type, key is null ? null : [key], system);
        var problem = new ButtonBinding(button, mode, action).Problem(Platform);
        Assert.Contains(expected, problem);
    }

    [Fact]
    public void AGestureNeedsItsActions()
    {
        Assert.Contains("five actions", new ButtonBinding("gesture", ButtonBinding.GestureMode).Problem(Platform));
    }

    [Fact]
    public void OldMappingsBecomeHoldBindings()
    {
        const string old = """{ "mappings": [ { "button": "forward", "key": "ControlLeft" } ] }""";
        var settings = JsonSerializer.Deserialize<AgentSettings>(old, Protocol.Json)!.Migrated();

        var binding = Assert.Single(settings.AllBindings);
        Assert.Equal("forward", binding.Button);
        Assert.Equal(ButtonBinding.ActionMode, binding.Mode);
        Assert.Equal(ActionTypes.Hold, binding.Action!.Type);
        Assert.Equal(["ControlLeft"], binding.Action.Keys);
        Assert.Null(settings.Mappings);
        Assert.True(settings.StartAtLogin);
    }

    [Fact]
    public void BindingsFromBeforeProfilesBecomeTheDefaultProfile()
    {
        const string old = """{ "bindings": [ { "button": "middle", "mode": "action", "action": { "type": "hold", "keys": ["ShiftLeft"] } } ] }""";
        var settings = JsonSerializer.Deserialize<AgentSettings>(old, Protocol.Json)!.Migrated();

        var profile = Assert.Single(settings.Profiles!);
        Assert.Equal(Profile.DefaultId, profile.Id);
        Assert.Equal(profile.Id, settings.ActiveProfile);
        Assert.Equal("middle", Assert.Single(settings.AllBindings).Button);
        Assert.Null(settings.Bindings);
        Assert.Same(settings, settings.Migrated());
    }

    [Fact]
    public void ChangesGoToTheActiveProfileOnly()
    {
        var work = new Profile("work", "Work", [ButtonBinding.Hold("middle", "ShiftLeft")]);
        var game = new Profile("game", "Game", [ButtonBinding.Hold("back", "ControlLeft")]);
        var settings = new AgentSettings(Profiles: [work, game], ActiveProfile: "game");

        var changed = settings.WithActive(p => p with { Bindings = [] });
        Assert.Empty(changed.AllBindings);
        Assert.Equal(work, changed.AllProfiles[0]);
        Assert.Equal("middle", Assert.Single((changed with { ActiveProfile = "work" }).AllBindings).Button);
    }

    [Fact]
    public void AMissingActiveProfileFallsBackToTheFirst()
    {
        Assert.Equal(Profile.DefaultId, AgentSettings.Empty.Active.Id);
        var settings = new AgentSettings(Profiles: [new Profile("a", "A")], ActiveProfile: "gone");
        Assert.Equal("a", settings.Active.Id);
    }

    [Fact]
    public void BindingsRoundTripAsCamelCaseJson()
    {
        var binding = new ButtonBinding("smartshift", ButtonBinding.ActionMode,
            new ButtonAction(ActionTypes.Press, ["MediaPlayPause"], Repeat: true));
        var json = JsonSerializer.Serialize(new[] { binding }, Protocol.Json);
        Assert.Contains("\"button\":\"smartshift\"", json);
        Assert.Contains("\"repeat\":true", json);
        Assert.DoesNotContain("isGesture", json);
        var back = JsonSerializer.Deserialize<ButtonBinding[]>(json, Protocol.Json)!;
        Assert.Equal(binding.Action!.Keys, back[0].Action!.Keys);
    }

    private static GestureActions Gesture(ButtonAction? doubleClick = null, ButtonAction? tripleClick = null) =>
        new(Press("ControlLeft", "KeyC"), ButtonAction.Nothing, ButtonAction.Nothing, ButtonAction.Nothing, ButtonAction.Nothing,
            doubleClick, tripleClick);

    [Fact]
    public void ClicksCountOnlyTheActionsSet()
    {
        var single = new ButtonBinding("middle", ButtonBinding.ActionMode, Press("KeyC"));
        Assert.Equal(1, single.Clicks.Most);
        Assert.Equal(1, (single with { DoubleClick = ButtonAction.Nothing }).Clicks.Most);
        Assert.Equal(2, (single with { DoubleClick = Press("KeyV") }).Clicks.Most);
        Assert.Equal(3, (single with { DoubleClick = Press("KeyV"), TripleClick = Press("Delete") }).Clicks.Most);

        var gesture = new ButtonBinding("gesture", ButtonBinding.GestureMode, Gesture: Gesture(Press("KeyV")));
        Assert.Equal(2, gesture.Clicks.Most);
        Assert.Equal(["KeyV"], gesture.Clicks[2].Keys);
        Assert.Equal(ButtonAction.Nothing, gesture.Clicks[3]);
    }

    [Fact]
    public void ATripleClickNeedsADoubleClick()
    {
        var binding = new ButtonBinding("middle", ButtonBinding.ActionMode, Press("KeyC"), TripleClick: Press("KeyV"));
        Assert.Contains("needs a double click", binding.Problem(Platform));
        var gesture = new ButtonBinding("gesture", ButtonBinding.GestureMode, Gesture: Gesture(tripleClick: Press("KeyV")));
        Assert.Contains("needs a double click", gesture.Problem(Platform));
    }

    [Fact]
    public void ClicksRunOnceWhenADoubleClickIsSet()
    {
        var hold = new ButtonBinding("middle", ButtonBinding.ActionMode, new ButtonAction(ActionTypes.Hold, ["ShiftLeft"]));
        Assert.Null(hold.Problem(Platform));
        Assert.Contains("runs once", (hold with { DoubleClick = Press("KeyV") }).Problem(Platform));

        Assert.Contains("runs once",
            new ButtonBinding("middle", ButtonBinding.ActionMode, Press("KeyC"), DoubleClick: Press("KeyV") with { Repeat = true }).Problem(Platform));
        Assert.Null(new ButtonBinding("middle", ButtonBinding.ActionMode, Press("KeyC"), DoubleClick: Press("KeyV")).Problem(Platform));
    }

    [Fact]
    public void ADoubleClickIsCheckedLikeAnyAction()
    {
        var binding = new ButtonBinding("middle", ButtonBinding.ActionMode, Press("KeyC"), DoubleClick: Press("F99"));
        Assert.Contains("can't send the key 'F99'", binding.Problem(Platform));
    }

    [Fact]
    public void GesturesFromBeforeMultiClicksLoadWithout()
    {
        const string json = """
            { "button": "gesture", "mode": "gesture", "gesture": {
              "click": { "type": "press", "keys": ["KeyC"] }, "up": { "type": "none" }, "down": { "type": "none" },
              "left": { "type": "none" }, "right": { "type": "none" } } }
            """;
        var binding = JsonSerializer.Deserialize<ButtonBinding>(json, Protocol.Json)!;
        Assert.Null(binding.Gesture!.DoubleClick);
        Assert.Null(binding.DoubleClick);
        Assert.Equal(1, binding.Clicks.Most);
    }

    [Fact]
    public void SettingsSaveOnlyTheirOwnFields()
    {
        var profile = new Profile("a", "A", [ButtonBinding.Hold("middle", "ShiftLeft")]);
        var json = JsonSerializer.Serialize(new AgentSettings(Profiles: [profile], ActiveProfile: "a"), Protocol.Json);
        Assert.DoesNotContain("allBindings", json);
        Assert.DoesNotContain("allProfiles", json);
        Assert.DoesNotContain("\"active\"", json);
        Assert.DoesNotContain("wheel", json);
        Assert.DoesNotContain("clicks", json);
        Assert.DoesNotContain("isSet", json);
        Assert.Contains("\"multiClickMs\":300", json);
    }

    private static string? Problem(ButtonAction right) =>
        new ButtonBinding("gesture", ButtonBinding.GestureMode,
            Gesture: new(Press("KeyC"), ButtonAction.Nothing, ButtonAction.Nothing, ButtonAction.Nothing, right)).Problem(Platform);

    [Fact]
    public void ADirectionCanHoldKeysOrRepeatButNotHoldTheLayer()
    {
        Assert.Null(Problem(new ButtonAction(ActionTypes.Hold, ["ShiftLeft"])));
        Assert.Null(Problem(Press("KeyC") with { Repeat = true }));
        Assert.Null(Problem(new ButtonAction(ActionTypes.Layer, Layer: LayerModes.Toggle)));
        Assert.Contains("not hold it", Problem(new ButtonAction(ActionTypes.Layer, Layer: LayerModes.Momentary)));
        Assert.Contains("can't send the key 'F99'", Problem(Press("F99")));
    }

    [Fact]
    public void GesturesWithStepsBeyondTheFirstLoadAsTheirFirstSteps()
    {
        const string json = """
            { "button": "gesture", "mode": "gesture", "gesture": {
              "click": { "type": "none" }, "up": { "type": "press", "keys": ["KeyC"], "right": { "type": "press", "keys": ["KeyV"] } },
              "down": { "type": "none" }, "left": { "type": "none" },
              "right": { "type": "none", "right": { "type": "press", "keys": ["KeyC"] } }, "spacingMm": 8 } }
            """;
        var binding = JsonSerializer.Deserialize<ButtonBinding>(json, Protocol.Json)!;
        Assert.Equal(["KeyC"], binding.Gesture!.Up.Keys);
        Assert.False(binding.Gesture.Right.IsSet);
        Assert.Null(binding.Problem(Platform));
        Assert.DoesNotContain("\"right\":{\"type\":\"none\",\"right\"", JsonSerializer.Serialize(binding, Protocol.Json));
    }

    private static readonly GesturePreset Copying = new("copying", "Copying", Press("ControlLeft", "KeyC"), Press("ControlLeft", "KeyV"),
        ButtonAction.Nothing, ButtonAction.Nothing);

    [Fact]
    public void AButtonFollowsItsPresetWithItsOwnClicks()
    {
        var binding = new ButtonBinding("gesture", ButtonBinding.GestureMode, Press("Delete"), DoubleClick: Press("KeyV"), Preset: "copying");
        Assert.Null(binding.Problem(Platform));

        var resolved = binding.Resolve(id => id == "copying" ? Copying : null, _ => null);
        Assert.Equal(["ControlLeft", "KeyC"], resolved.Gesture!.Up.Keys);
        Assert.Equal(["Delete"], resolved.Gesture.Click.Keys);
        Assert.Equal(2, resolved.Clicks.Most);
        Assert.Null(resolved.Problem(Platform));
    }

    [Fact]
    public void AButtonWhosePresetIsGoneRunsJustItsClicks()
    {
        var resolved = new ButtonBinding("gesture", ButtonBinding.GestureMode, Press("Delete"), Preset: "gone").Resolve(_ => null, _ => null);
        Assert.False(resolved.IsGesture);
        Assert.Equal(["Delete"], resolved.Clicks.Single.Keys);
    }

    [Fact]
    public void AGestureFollowingAPresetNeedsAClick()
    {
        Assert.Contains("click", new ButtonBinding("gesture", ButtonBinding.GestureMode, Preset: "copying").Problem(Platform));
    }

    [Fact]
    public void GesturesFromBeforePresetsFollowOneAfterMigrating()
    {
        var gesture = new GestureActions(Press("KeyC"), Press("ControlLeft", "KeyC"), Press("ControlLeft", "KeyV"),
            ButtonAction.Nothing, ButtonAction.Nothing, DoubleClick: Press("Delete"));
        var work = new Profile("work", "Work", [new ButtonBinding("gesture", ButtonBinding.GestureMode, Gesture: gesture)]);
        var game = new Profile("game", "Game", [new ButtonBinding("middle", ButtonBinding.GestureMode, Gesture: gesture with { Click = Press("KeyV") })]);
        var settings = new AgentSettings(Profiles: [work, game], ActiveProfile: "work").Migrated();

        var preset = Assert.Single(settings.GesturePresets!);
        Assert.Equal("Gesture button (Work)", preset.Name);
        var first = settings.AllProfiles[0].AllBindings[0];
        Assert.Equal(preset.Id, first.Preset);
        Assert.Null(first.Gesture);
        Assert.Equal(ActionTypes.Press, first.Action!.Type);
        Assert.Equal(["KeyC"], first.Action.Keys);
        Assert.Equal(ActionTypes.Press, first.DoubleClick!.Type);
        Assert.Equal(["Delete"], first.DoubleClick.Keys);
        Assert.Empty(settings.Macros ?? []);
        Assert.Equal(preset.Id, settings.AllProfiles[1].AllBindings[0].Preset);
        Assert.Same(settings, settings.Migrated());
    }

    [Fact]
    public void AGestureMatchingABuiltInPresetFollowsIt()
    {
        var media = PresetLibrary.BuiltIn.Single(p => p.Id == "media");
        var gesture = media.With(Press("MediaPlayPause"), null, null);
        var settings = new AgentSettings(Profiles: [new Profile("a", "A", [new ButtonBinding("gesture", ButtonBinding.GestureMode, Gesture: gesture)])])
            .Migrated();
        Assert.Empty(settings.GesturePresets!);
        Assert.Equal("media", settings.AllBindings[0].Preset);
    }

    [Fact]
    public void AnEditedBuiltInPresetReplacesTheOriginal()
    {
        var desktops = PresetLibrary.BuiltIn[0] with { Up = Press("KeyC") };
        var settings = new AgentSettings(GesturePresets: [desktops, Copying]);
        Assert.Equal(PresetLibrary.BuiltIn.Count + 1, settings.AllPresets.Count);
        Assert.Equal(["KeyC"], settings.FindPreset(desktops.Id)!.Up.Keys);
        Assert.Equal("copying", settings.AllPresets[^1].Id);
        Assert.True(PresetLibrary.SameMoves(desktops, desktops with { Name = "Other", Id = "x" }));
        Assert.False(PresetLibrary.SameMoves(desktops, PresetLibrary.BuiltIn[0]));
    }
}
