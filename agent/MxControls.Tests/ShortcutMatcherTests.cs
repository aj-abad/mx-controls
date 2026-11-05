using MxControls.Input;

namespace MxControls.Tests;

public class ShortcutMatcherTests
{
    private static readonly string[] CtrlAlt1 = ["ControlLeft", "AltLeft", "Digit1"];

    private static (ShortcutMatcher Matcher, List<int> Fired) Make(params (int, IReadOnlyList<string>)[] shortcuts)
    {
        var matcher = new ShortcutMatcher();
        var fired = new List<int>();
        matcher.Fired += fired.Add;
        matcher.Set(shortcuts);
        return (matcher, fired);
    }

    [Fact]
    public void FiresAndHidesOnlyTheKeyWithExactlyItsModifiers()
    {
        var (matcher, fired) = Make((2, CtrlAlt1));

        Assert.False(matcher.OnKey("AltLeft", true));
        Assert.False(matcher.OnKey("ControlLeft", true));
        Assert.True(matcher.OnKey("Digit1", true));
        Assert.Equal([2], fired);
        Assert.True(matcher.OnKey("Digit1", false));
        Assert.False(matcher.OnKey("ControlLeft", false));
        Assert.False(matcher.OnKey("AltLeft", false));
    }

    [Theory]
    [InlineData("ControlLeft")]
    [InlineData("ControlRight", "AltLeft")]
    [InlineData("ControlLeft", "AltLeft", "ShiftLeft")]
    public void OtherModifiersLeaveTheKeyAlone(params string[] held)
    {
        var (matcher, fired) = Make((2, CtrlAlt1));

        foreach (var key in held)
            matcher.OnKey(key, true);

        Assert.False(matcher.OnKey("Digit1", true));
        Assert.False(matcher.OnKey("Digit1", false));
        Assert.Empty(fired);
    }

    [Fact]
    public void ARepeatStaysHiddenWithoutFiringAgain()
    {
        var (matcher, fired) = Make((2, CtrlAlt1));
        matcher.OnKey("ControlLeft", true);
        matcher.OnKey("AltLeft", true);

        Assert.True(matcher.OnKey("Digit1", true));
        Assert.True(matcher.OnKey("Digit1", true));
        // Letting go of a modifier first doesn't let the rest of the press through.
        matcher.OnKey("AltLeft", false);
        Assert.True(matcher.OnKey("Digit1", true));
        Assert.True(matcher.OnKey("Digit1", false));

        Assert.Equal([2], fired);
    }

    [Fact]
    public void WhileSuspendedKeysPassAndNothingFires()
    {
        var (matcher, fired) = Make((2, CtrlAlt1));
        matcher.OnKey("ControlLeft", true);
        matcher.OnKey("AltLeft", true);

        matcher.Suspended = true;
        Assert.False(matcher.OnKey("Digit1", true));
        Assert.False(matcher.OnKey("Digit1", false));
        matcher.Suspended = false;
        Assert.True(matcher.OnKey("Digit1", true));

        Assert.Equal([2], fired);
    }

    [Fact]
    public void AKeyHeldWhenSuspendingStillHidesItsRelease()
    {
        var (matcher, _) = Make((2, CtrlAlt1));
        matcher.OnKey("ControlLeft", true);
        matcher.OnKey("AltLeft", true);
        matcher.OnKey("Digit1", true);

        matcher.Suspended = true;

        Assert.True(matcher.OnKey("Digit1", false));
    }

    [Fact]
    public void ReplacingTheShortcutsTakesEffect()
    {
        var (matcher, fired) = Make((2, CtrlAlt1));
        matcher.OnKey("ControlLeft", true);
        matcher.OnKey("AltLeft", true);

        matcher.Set([]);
        Assert.False(matcher.OnKey("Digit1", true));
        matcher.OnKey("Digit1", false);

        Assert.Empty(fired);
    }

    [Fact]
    public void ABareFunctionKeyWorks()
    {
        var (matcher, fired) = Make((1, ["F13"]));

        Assert.True(matcher.OnKey("F13", true));

        Assert.Equal([1], fired);
    }

    [Theory]
    [InlineData("F13")] [InlineData("F14")] [InlineData("F15")] [InlineData("F16")]
    [InlineData("F17")] [InlineData("F18")] [InlineData("F19")] [InlineData("F20")]
    [InlineData("F21")] [InlineData("F22")] [InlineData("F23")] [InlineData("F24")]
    public void TheHighFunctionKeysWorkAloneAndWithModifiers(string key)
    {
        Assert.Null(ShortcutMatcher.Problem([key]));
        Assert.Null(ShortcutMatcher.Problem(["ControlLeft", key]));

        var (matcher, fired) = Make((1, [key]), (2, ["ControlLeft", key]));
        Assert.True(matcher.OnKey(key, true));
        Assert.True(matcher.OnKey(key, false));
        matcher.OnKey("ControlLeft", true);
        Assert.True(matcher.OnKey(key, true));

        Assert.Equal([1, 2], fired);
    }

    [Fact]
    public void KeysWithoutACodeAreIgnored()
    {
        var (matcher, fired) = Make((2, CtrlAlt1));

        Assert.False(matcher.OnKey(null, true));

        Assert.Empty(fired);
    }

    [Theory]
    [InlineData("ControlLeft", "KeyK")]
    [InlineData("MetaLeft", "AltLeft", "Digit1")]
    [InlineData("F5")]
    [InlineData("F13")]
    public void AcceptsAKeyWithModifiersOrAFunctionKey(params string[] keys) =>
        Assert.Null(ShortcutMatcher.Problem(keys));

    [Theory]
    [InlineData]
    [InlineData("KeyK")]
    [InlineData("Forward")]
    [InlineData("ControlLeft")]
    [InlineData("ControlLeft", "AltLeft")]
    [InlineData("KeyK", "KeyL")]
    [InlineData("KeyK", "ControlLeft")]
    [InlineData("ControlLeft", "CapsLock")]
    public void RefusesEverythingElse(params string[] keys) =>
        Assert.NotNull(ShortcutMatcher.Problem(keys));

    [Fact]
    public void TheOrderOfModifiersDoesNotMakeAnotherShortcut() =>
        Assert.True(ShortcutMatcher.Same(["ControlLeft", "AltLeft", "KeyK"], ["AltLeft", "ControlLeft", "KeyK"]));
}

public class HostShortcutSettingsTests
{
    [Fact]
    public void SurviveBeingSavedAndRead()
    {
        var info = MxControls.Agent.Protocol.Json.GetTypeInfo(typeof(MxControls.Agent.AgentSettings));
        var settings = new MxControls.Agent.AgentSettings(HostShortcuts: [new(1, ["ControlLeft", "Digit2"])]);

        var read = (MxControls.Agent.AgentSettings)System.Text.Json.JsonSerializer.Deserialize(
            System.Text.Json.JsonSerializer.Serialize(settings, info), info)!;

        var shortcut = Assert.Single(read.HostShortcuts!);
        Assert.Equal(1, shortcut.Host);
        Assert.Equal(["ControlLeft", "Digit2"], shortcut.Keys);
    }
}
