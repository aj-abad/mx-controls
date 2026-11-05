using System.Text.Json;
using MxControls.Actions;
using MxControls.Agent;

namespace MxControls.Tests;

public class LayerTests
{
    private static readonly FakePlatform Platform = new();
    private const ushort Middle = 0x52;
    private const ushort Back = 0x53;

    private static ButtonAction Press(params string[] keys) => new(ActionTypes.Press, keys);
    private static readonly ButtonAction HoldLayer = new(ActionTypes.Layer, Layer: LayerModes.Momentary);
    private static readonly ButtonAction ToggleLayer = new(ActionTypes.Layer, Layer: LayerModes.Toggle);

    private static LayerSwitch Switch(out ButtonBinding main, out ButtonBinding second)
    {
        main = new ButtonBinding("middle", ButtonBinding.ActionMode, Press("KeyC"));
        second = new ButtonBinding("middle", ButtonBinding.ActionMode, Press("KeyV"));
        var layers = new LayerSwitch();
        layers.Set(new Dictionary<ushort, ButtonBinding> { [Middle] = main },
            new Dictionary<ushort, ButtonBinding> { [Middle] = second, [Back] = second with { Button = "back" } });
        return layers;
    }

    [Fact]
    public void TheSecondLayerRunsWhileHeldAndFallsBackToTheMainOne()
    {
        var layers = Switch(out var main, out var second);
        Assert.Same(main, layers[Middle]);
        Assert.Null(layers[Back]);

        Assert.True(layers.Hold(Back));
        Assert.Same(second, layers[Middle]);
        Assert.Equal("back", layers[Back]!.Button);

        Assert.False(layers.Hold(Middle));
        Assert.False(layers.Release(Back));
        Assert.True(layers.Release(Middle));
        Assert.Same(main, layers[Middle]);
    }

    [Fact]
    public void AToggleStaysOnUntilToggledOrReset()
    {
        var layers = Switch(out _, out var second);
        Assert.True(layers.Toggle());
        Assert.Same(second, layers[Middle]);
        // A hold and a release while it's toggled on change nothing.
        Assert.False(layers.Hold(Back));
        Assert.False(layers.Release(Back));
        Assert.True(layers.Active);
        Assert.False(layers.ReleaseAll());
        Assert.True(layers.Reset());
        Assert.False(layers.Active);
    }

    [Fact]
    public void BothLayersAreDiverted()
    {
        var layers = Switch(out _, out _);
        Assert.Equal([Middle, Back], layers.Controls.Order());
    }

    [Fact]
    public void LayerActionsAreChecked()
    {
        Assert.Null(new ButtonBinding("middle", ButtonBinding.ActionMode, HoldLayer).Problem(Platform));
        Assert.Null(new ButtonBinding("middle", ButtonBinding.ActionMode, ToggleLayer, DoubleClick: Press("KeyC")).Problem(Platform));
        Assert.Contains("Unknown layer mode",
            new ButtonBinding("middle", ButtonBinding.ActionMode, ToggleLayer with { Layer = "sideways" }).Problem(Platform));
        Assert.Contains("can't repeat",
            new ButtonBinding("middle", ButtonBinding.ActionMode, ToggleLayer with { Repeat = true }).Problem(Platform));
        Assert.Contains("hold keys or a layer",
            new ButtonBinding("middle", ButtonBinding.ActionMode, HoldLayer, DoubleClick: Press("KeyC")).Problem(Platform));
    }

    [Fact]
    public void AGestureCanToggleTheLayerButNotHoldIt()
    {
        var click = new ButtonBinding("gesture", ButtonBinding.GestureMode, HoldLayer, Preset: "desktops");
        Assert.Contains("can toggle the second layer but not hold it", click.Problem(Platform));
        Assert.Null((click with { Action = ToggleLayer }).Problem(Platform));

        var gesture = new GestureActions(Press("KeyC"), GestureStep.Of(HoldLayer), GestureStep.Nothing, GestureStep.Nothing, GestureStep.Of(ToggleLayer));
        Assert.Contains("can toggle the second layer but not hold it",
            new ButtonBinding("gesture", ButtonBinding.GestureMode, Gesture: gesture).Problem(Platform));
        Assert.Null(new ButtonBinding("gesture", ButtonBinding.GestureMode,
            Gesture: gesture with { Up = GestureStep.Nothing }).Problem(Platform));
    }

    [Fact]
    public void TheThumbWheelSwitchesNeitherLayersNorProfiles()
    {
        var wheel = new ThumbWheelSettings(ThumbWheelSettings.ActionsMode, Left: ToggleLayer);
        Assert.Contains("can't switch layers or profiles", wheel.Problem(Platform));
        Assert.Contains("can't switch layers or profiles",
            (wheel with { Left = null, Right = new ButtonAction(ActionTypes.Profile, Profile: "work") }).Problem(Platform));
    }

    [Fact]
    public void AProfileActionNeedsAProfile()
    {
        var action = new ButtonAction(ActionTypes.Profile, Profile: "work");
        Assert.Null(new ButtonBinding("back", ButtonBinding.ActionMode, action).Problem(Platform));
        Assert.Contains("needs a profile",
            new ButtonBinding("back", ButtonBinding.ActionMode, action with { Profile = null }).Problem(Platform));
    }

    [Fact]
    public void GestureStepsCarryLayerAndProfile()
    {
        var step = GestureStep.Of(new ButtonAction(ActionTypes.Profile, Profile: "work"));
        Assert.Equal("work", step.Action.Profile);
        Assert.Equal(LayerModes.Toggle, GestureStep.Of(ToggleLayer).Map(a => a).Action.Layer);
    }

    [Fact]
    public void ProfilesRoundTripWithTheirLayerAndPrograms()
    {
        var profile = new Profile("work", "Work", [ButtonBinding.Hold("middle", "ShiftLeft")],
            Layer: new ProfileLayer([new ButtonBinding("back", ButtonBinding.ActionMode, ToggleLayer)]), Programs: ["code.exe"]);
        var json = JsonSerializer.Serialize(new AgentSettings(Profiles: [profile]), Protocol.Json);
        Assert.Contains("\"programs\":[\"code.exe\"]", json);
        Assert.Contains("\"layer\":\"toggle\"", json);
        Assert.DoesNotContain("secondLayer", json);
        Assert.DoesNotContain("switchable", json);
        var back = JsonSerializer.Deserialize<AgentSettings>(json, Protocol.Json)!.AllProfiles[0];
        Assert.Equal(["code.exe"], back.AllPrograms);
        Assert.Equal(LayerModes.Toggle, back.SecondLayer.AllBindings[0].Action!.Layer);
    }
}

public class ProgramProfileTests
{
    private static readonly Profile Home = new("home", "Home");
    private static readonly Profile Work = new("work", "Work");
    private static readonly Profile Code = new("code", "Code", Programs: ["code.exe", "Devenv.exe"]);

    [Fact]
    public void AProgramRunsItsOwnProfileAndEverythingElseTheChosenOne()
    {
        var settings = new AgentSettings(Profiles: [Home, Code, Work], ActiveProfile: "work");
        Assert.Equal("code", settings.Running("code.exe").Id);
        Assert.Equal("code", settings.Running("devenv.exe").Id);
        Assert.Equal("work", settings.Running("chrome.exe").Id);
        Assert.Equal("work", settings.Running(null).Id);
    }

    [Fact]
    public void TheChosenProfileIsNeverOneThatFollowsPrograms()
    {
        var settings = new AgentSettings(Profiles: [Code, Home, Work], ActiveProfile: "code");
        Assert.Equal("home", settings.Active.Id);
        Assert.Equal("home", (settings with { ActiveProfile = "gone" }).Active.Id);
        Assert.Equal("home", settings.Running("chrome.exe").Id);
    }

    [Fact]
    public void EditingAProfileLeavesTheChosenOneAlone()
    {
        var settings = new AgentSettings(Profiles: [Home, Code], ActiveProfile: "home");
        var changed = settings.WithProfile("code", p => p with { Bindings = [ButtonBinding.Hold("back", "ShiftLeft")] });
        Assert.Equal("home", changed.Active.Id);
        Assert.Single(changed.FindProfile("code")!.AllBindings);
        Assert.Empty(changed.FindProfile("home")!.AllBindings);
    }
}
