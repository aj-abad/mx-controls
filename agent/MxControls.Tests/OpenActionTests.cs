using System.Text.Json;
using MxControls.Actions;
using MxControls.Agent;
using MxControls.Platform;

namespace MxControls.Tests;

public class OpenActionTests
{
    private sealed class Apps : IApps
    {
        public List<string> Opened { get; } = [];
        public IReadOnlyList<AppInfo> Installed() => [new("Chrome", "Google Chrome")];
        public void Open(string target) => Opened.Add(target);
    }

    private sealed class Shortcuts : IShortcuts
    {
        public List<string> Ran { get; } = [];
        public IReadOnlyList<ShortcutInfo> List() => [new("ID-1", "Focus")];
        public void Run(string id) => Ran.Add(id);
    }

    /// <summary>A platform that opens things and runs Shortcuts, as a Mac does.</summary>
    private sealed class OpeningPlatform : IPlatform
    {
        private readonly FakePlatform _inner = new();
        public Apps AppsSeen { get; } = new();
        public Shortcuts ShortcutsSeen { get; } = new();
        public IHidBackend Hid => _inner.Hid;
        public IInputInjector Input => _inner;
        public ILoginItem LoginItem => _inner.LoginItem;
        public IPrograms? Programs => null;
        public IKeyCapture? KeyCapture => null;
        public IKeyWatch? KeyWatch => null;
        public IApps? Apps => AppsSeen;
        public IShortcuts? Shortcuts => ShortcutsSeen;
        public bool SupportsSystem(string name) => _inner.SupportsSystem(name);
        public void RunSystem(string name) => _inner.RunSystem(name);
        public bool IsOptionsPlusRunning() => false;
    }

    private static ButtonAction Open(string target) => new(ActionTypes.Open, Open: target);

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("mailto:me@example.com", true)]
    [InlineData("obsidian://open?vault=Notes", true)]
    [InlineData(@"C:\Users\Me\Documents", false)]
    [InlineData("/Users/me/Notes.txt", false)]
    [InlineData("~/Notes.txt", false)]
    [InlineData("app:Chrome", false)]
    public void LinksAreToldFromPathsAndApps(string target, bool link)
    {
        Assert.Equal(link, OpenTarget.IsLink(target));
        Assert.Equal(target.StartsWith("app:"), OpenTarget.IsApp(target));
    }

    [Fact]
    public void OpenActionsAreChecked()
    {
        var platform = new OpeningPlatform();
        Assert.Null(Open("app:Chrome").Problem(platform));
        Assert.Null(Open("https://example.com").Problem(platform));
        Assert.Null(Open(@"C:\Users\Me").Problem(platform));
        Assert.Contains("something to open", Open("").Problem(platform));
        Assert.Contains("something to open", Open("app:").Problem(platform));
        Assert.Contains("whole web address", Open("https://").Problem(platform));
        Assert.Contains("can't repeat", (Open("app:Chrome") with { Repeat = true }).Problem(platform));
        // Without a way to open things, as a platform may have, the action is refused rather than doing nothing.
        Assert.Contains("can't open", Open("app:Chrome").Problem(new FakePlatform()));
    }

    [Fact]
    public void ShortcutsRunOnlyWhereThereIsAShortcutsApp()
    {
        var action = new ButtonAction(ActionTypes.AppleShortcut, Shortcut: "ID-1", Name: "Focus");
        Assert.Null(action.Problem(new OpeningPlatform()));
        Assert.Contains("only on a Mac", action.Problem(new FakePlatform()));
        Assert.Contains("needs a Shortcut", (action with { Shortcut = " " }).Problem(new OpeningPlatform()));
    }

    [Fact]
    public async Task OpeningAndShortcutsRunOffTheMouseThread()
    {
        var platform = new OpeningPlatform();
        var runner = new ActionRunner(platform, new object());
        runner.Run(Open("app:Chrome"));
        runner.Begin(1, new ButtonAction(ActionTypes.AppleShortcut, Shortcut: "ID-1"));
        runner.End(1);
        for (int i = 0; i < 100 && (platform.AppsSeen.Opened.Count == 0 || platform.ShortcutsSeen.Ran.Count == 0); i++)
            await Task.Delay(10);
        Assert.Equal(["app:Chrome"], platform.AppsSeen.Opened);
        Assert.Equal(["ID-1"], platform.ShortcutsSeen.Ran);
    }

    [Fact]
    public void TheNewFieldsRoundTripThroughTheWireFormat()
    {
        var json = """[{"button":"middle","mode":"action","action":{"type":"appleShortcut","shortcut":"ID-1","name":"Focus"}}]""";
        var binding = JsonSerializer.Deserialize<ButtonBinding[]>(json, Protocol.Json)![0];
        Assert.Equal("ID-1", binding.Action!.Shortcut);
        Assert.Contains("\"name\":\"Focus\"", JsonSerializer.Serialize(new[] { binding }, Protocol.Json));
    }

    [Fact]
    public void EverySystemActionNameIsDistinct()
    {
        Assert.Equal(SystemActions.All.Length, SystemActions.All.Distinct().Count());
        Assert.Contains(SystemActions.TaskView, SystemActions.All);
    }
}
