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

    [Fact]
    public void SettingsSaveOnlyTheirOwnFields()
    {
        var profile = new Profile("a", "A", [ButtonBinding.Hold("middle", "ShiftLeft")]);
        var json = JsonSerializer.Serialize(new AgentSettings(Profiles: [profile], ActiveProfile: "a"), Protocol.Json);
        Assert.DoesNotContain("allBindings", json);
        Assert.DoesNotContain("allProfiles", json);
        Assert.DoesNotContain("\"active\"", json);
        Assert.DoesNotContain("wheel", json);
    }
}
