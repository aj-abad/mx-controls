using MxControls.Actions;
using MxControls.Agent;

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
    public void RecordedKeysBecomeSharedMacrosAndBuiltInActionsStay()
    {
        var custom = new ButtonAction(ActionTypes.Press, ["ShiftLeft", "Delete"], Repeat: true);
        var copy = new ButtonAction(ActionTypes.Press, [OperatingSystem.IsMacOS() ? "MetaLeft" : "ControlLeft", "KeyC"]);
        var work = new Profile("work", "Work", [new ButtonBinding("middle", ButtonBinding.ActionMode, custom, DoubleClick: copy)],
            new ThumbWheelSettings(ThumbWheelSettings.ActionsMode, Left: custom with { Repeat = false }));
        var preset = new GesturePreset("p", "P", GestureStep.Of(custom with { Repeat = false }), GestureStep.Nothing, GestureStep.Nothing, GestureStep.Nothing);
        var settings = new AgentSettings(Profiles: [work], GesturePresets: [preset]).Migrated();

        var macro = Assert.Single(settings.Macros!);
        Assert.Equal("Shift + Delete", macro.Name);
        var binding = settings.AllBindings[0];
        Assert.Equal(new ButtonAction(ActionTypes.Macro, Repeat: true, Macro: macro.Id), binding.Action);
        Assert.Equal(copy.Keys, binding.DoubleClick!.Keys);
        Assert.Equal(macro.Id, settings.Active.ThumbWheel!.Left!.Macro);
        Assert.Equal(macro.Id, settings.GesturePresets![0].Up.Macro);
        Assert.Same(settings, settings.Migrated());
    }

    [Fact]
    public void MacroLabelsReadLikeTheKeys()
    {
        Assert.Equal("Shift + 5", MacroMigration.Label(["ShiftLeft", "Digit5"]));
        Assert.Equal("F5", MacroMigration.Label(["F5"]));
    }
}
