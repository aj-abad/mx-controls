using System.Text.Json.Serialization;
using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// A named sequence of input that actions run by its ID, so editing it changes every button, gesture step and
/// thumb wheel action that runs it. Macros belong to no profile.
/// </summary>
public sealed record Macro(string Id, string Name, IReadOnlyList<MacroStep> Steps)
{
    public const int MaxSteps = 200;
    // A button press is expected to finish at once; a macro that ran on for longer would outlast the press that
    // started it and pile up behind the next one.
    public const int MaxTotalMs = 1_000;
    public const int MaxTextLength = 2_000;

    /// <summary>Whether it waits anywhere, so it has to run in the background rather than at once.</summary>
    [JsonIgnore]
    public bool Waits => Steps.Any(s => s is { Type: MacroStepTypes.Wait, Ms: > 0 });

    public string? Problem(IPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "A macro needs a name.";
        if (Steps.Count == 0)
            return "A macro needs at least one step.";
        if (Steps.Count > MaxSteps)
            return $"A macro has at most {MaxSteps} steps.";
        if (Steps.Where(s => s.Type == MacroStepTypes.Wait).Sum(s => (long)s.Ms) > MaxTotalMs)
            return "A macro lasts at most 1 second, waits included.";
        return Steps.Select(s => s.Problem(platform)).FirstOrDefault(p => p is not null);
    }
}

/// <summary>
/// One step of a <see cref="Macro"/>: <c>keys</c> taps <see cref="Keys"/> together, <c>text</c> types
/// <see cref="Text"/> as it's written whatever the keyboard layout, <c>wait</c> pauses <see cref="Ms"/>, and
/// <c>click</c> clicks the mouse button <see cref="Mouse"/>.
/// </summary>
public sealed record MacroStep(string Type, IReadOnlyList<string>? Keys = null, string? Text = null, int Ms = 0, string? Mouse = null)
{
    public string? Problem(IPlatform platform) => Type switch
    {
        MacroStepTypes.Keys when Keys is not { Count: > 0 } => "A keys step needs at least one key.",
        MacroStepTypes.Keys => Keys.FirstOrDefault(k => !platform.Input.Supports(k)) is { } key ? $"This platform can't send the key '{key}'." : null,
        MacroStepTypes.Text when string.IsNullOrEmpty(Text) => "A text step needs some text.",
        MacroStepTypes.Text when Text.Length > Macro.MaxTextLength => $"A text step is at most {Macro.MaxTextLength} characters.",
        MacroStepTypes.Text => null,
        MacroStepTypes.Wait when Ms is < 1 or > Macro.MaxTotalMs => $"A wait is 1 to {Macro.MaxTotalMs} ms.",
        MacroStepTypes.Wait => null,
        MacroStepTypes.Click => Mouse is not null && platform.Input.SupportsMouse(Mouse) ? null : $"Unknown mouse button '{Mouse}'.",
        _ => $"Unknown macro step '{Type}'.",
    };
}

public static class MacroStepTypes
{
    public const string Keys = "keys";
    public const string Text = "text";
    public const string Wait = "wait";
    public const string Click = "click";
}

/// <summary>The macros actions can run, swapped whole when they change, so a running engine always sees a complete set.</summary>
public sealed class MacroLibrary
{
    private volatile IReadOnlyDictionary<string, Macro> _macros = new Dictionary<string, Macro>();

    public Macro? Find(string id) => _macros.GetValueOrDefault(id);

    public void Set(IEnumerable<Macro> macros) => _macros = macros.ToDictionary(m => m.Id);
}
