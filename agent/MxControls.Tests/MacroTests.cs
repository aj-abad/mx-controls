using MxControls.Actions;
using MxControls.Agent;
using System.Text.Json;

namespace MxControls.Tests;

public class MacroTests
{
    private const ushort Button = 0x00C3;
    private static readonly FakePlatform Platform = new();

    private static MacroStep Keys(params string[] keys) => new(MacroStepTypes.Keys, keys);
    private static MacroStep Wait(int ms) => new(MacroStepTypes.Wait, Ms: ms);
    private static ButtonAction Run(string id, bool repeat = false) => new(ActionTypes.Macro, Repeat: repeat, Macro: id);

    private static (ActionRunner Runner, FakePlatform Platform, object Gate) Create(params Macro[] macros)
    {
        var library = new MacroLibrary();
        library.Set(macros);
        var platform = new FakePlatform();
        var gate = new object();
        return (new ActionRunner(platform, gate, library), platform, gate);
    }

    [Fact]
    public void AMacroWithoutWaitsRunsAtOnce()
    {
        var (runner, platform, _) = Create(new Macro("m", "M", [Keys("ControlLeft", "KeyC"), new MacroStep(MacroStepTypes.Text, Text: "hi")]));
        runner.Run(Run("m"));
        Assert.Equal(["down ControlLeft", "down KeyC", "up KeyC", "up ControlLeft", "type hi"], platform.Log);
    }

    [Fact]
    public async Task AMacroThatWaitsRunsInTheBackgroundInOrder()
    {
        var (runner, platform, _) = Create(new Macro("m", "M", [Keys("KeyC"), Wait(80), Keys("KeyV")]));
        runner.Run(Run("m"));
        await Task.Delay(20);
        Assert.Equal(["down KeyC", "up KeyC"], platform.Log);
        await Task.Delay(200);
        Assert.Equal(["down KeyC", "up KeyC", "down KeyV", "up KeyV"], platform.Log);
    }

    [Fact]
    public async Task MacrosThatWaitRunOneAtATime()
    {
        var (runner, platform, _) = Create(new Macro("m", "M", [Keys("KeyC"), Wait(60), Keys("KeyV")]));
        runner.Run(Run("m"));
        runner.Run(Run("m"));
        await Task.Delay(400);
        Assert.Equal(["down KeyC", "up KeyC", "down KeyV", "up KeyV", "down KeyC", "up KeyC", "down KeyV", "up KeyV"], platform.Log);
    }

    [Fact]
    public void AMissingMacroDoesNothing()
    {
        var (runner, platform, _) = Create();
        runner.Begin(Button, Run("gone"));
        runner.End(Button);
        Assert.Empty(platform.Log);
    }

    [Fact]
    public void AMacroLastsAtMostOneSecond()
    {
        Assert.Null(new Macro("m", "M", [Keys("KeyC"), Wait(600), Wait(400)]).Problem(Platform));
        Assert.Contains("1 second", new Macro("m", "M", [Keys("KeyC"), Wait(600), Wait(401)]).Problem(Platform));
        Assert.Contains("1 to 1000 ms", new Macro("m", "M", [Wait(0)]).Problem(Platform));
    }

    [Fact]
    public void ExplainsWhatIsWrongWithAMacro()
    {
        Assert.Contains("name", new Macro("m", " ", [Keys("KeyC")]).Problem(Platform));
        Assert.Contains("at least one step", new Macro("m", "M", []).Problem(Platform));
        Assert.Contains("can't send the key 'F99'", new Macro("m", "M", [Keys("F99")]).Problem(Platform));
        Assert.Contains("some text", new Macro("m", "M", [new MacroStep(MacroStepTypes.Text, Text: "")]).Problem(Platform));
        Assert.Contains("mouse button", new Macro("m", "M", [new MacroStep(MacroStepTypes.Click, Mouse: "left")]).Problem(Platform));
        Assert.Null(new Macro("m", "M", [new MacroStep(MacroStepTypes.Click, Mouse: MouseButtons.Back)]).Problem(Platform));
    }

    [Fact]
    public void AMacroActionNeedsAMacro()
    {
        Assert.Contains("needs a macro", new ButtonBinding("middle", ButtonBinding.ActionMode, new ButtonAction(ActionTypes.Macro)).Problem(Platform));
        Assert.Null(new ButtonBinding("middle", ButtonBinding.ActionMode, Run("m", repeat: true)).Problem(Platform));
    }

    [Fact]
    public void InlineShortcutsSurviveSettingsRoundTripWithoutBecomingMacros()
    {
        var custom = new ButtonAction(ActionTypes.Press, ["ShiftLeft", "Delete"], Repeat: true);
        var copy = new ButtonAction(ActionTypes.Press, [OperatingSystem.IsMacOS() ? "MetaLeft" : "ControlLeft", "KeyC"]);
        var work = new Profile("work", "Work", [new ButtonBinding("middle", ButtonBinding.ActionMode, custom, DoubleClick: copy)],
            new ThumbWheelSettings(ThumbWheelSettings.ActionsMode, Left: custom with { Repeat = false }));
        var preset = new GesturePreset("p", "P", custom with { Repeat = false }, ButtonAction.Nothing, ButtonAction.Nothing, ButtonAction.Nothing);
        var before = new AgentSettings(Profiles: [work], GesturePresets: [preset]);
        var json = JsonSerializer.Serialize(before, Protocol.Json);
        var settings = JsonSerializer.Deserialize<AgentSettings>(json, Protocol.Json)!.Migrated();

        Assert.Empty(settings.Macros ?? []);
        var binding = settings.AllBindings[0];
        Assert.Equal(ActionTypes.Press, binding.Action!.Type);
        Assert.Equal(custom.Keys, binding.Action.Keys);
        Assert.True(binding.Action.Repeat);
        Assert.Equal(copy.Keys, binding.DoubleClick!.Keys);
        Assert.Equal(ActionTypes.Press, settings.Active.ThumbWheel!.Left!.Type);
        Assert.Equal(custom.Keys, settings.Active.ThumbWheel.Left.Keys);
        Assert.False(settings.Active.ThumbWheel.Left.Repeat);
        Assert.Equal(ActionTypes.Press, settings.GesturePresets![0].Up.Type);
        Assert.Equal(custom.Keys, settings.GesturePresets[0].Up.Keys);
        Assert.Same(settings, settings.Migrated());
    }

    [Fact]
    public void ExistingSharedMacrosStaySharedEvenWithOnlyOneCombination()
    {
        var macro = new Macro("shared", "Shared shortcut", [Keys("ShiftLeft", "Delete")]);
        var profile = new Profile("work", "Work", [new ButtonBinding("middle", ButtonBinding.ActionMode, Run(macro.Id))]);
        var settings = new AgentSettings(Profiles: [profile], Macros: [macro]);
        Assert.Same(settings, settings.Migrated());
        Assert.Same(macro, Assert.Single(settings.Macros!));
        Assert.Equal(ActionTypes.Macro, settings.AllBindings[0].Action!.Type);
        Assert.Equal(macro.Id, settings.AllBindings[0].Action!.Macro);
    }
}
