using System.Text.Json;
using MxControls.Actions;
using MxControls.Agent;

namespace MxControls.Tests;

public class RoseLibraryTests
{
    private static readonly FakePlatform Platform = new();
    private static int Mm(double mm) => (int)Math.Round(mm / 25.4 * 1000);

    private static ButtonAction Press(params string[] keys) => new(ActionTypes.Press, keys);

    private static RoseItem Option(string label) => new(Press("KeyC"), label);

    private static RoseItem Folder(string label, params RoseItem[] items) => new(ButtonAction.Nothing, label, Items: items);

    [Fact]
    public void FoldersNestAndAreCheckedAllTheWayDown()
    {
        var menu = new RoseMenu([Option("Copy"), Folder("More", Option("A"), Folder("Deeper", Option("B"), Option("C")))]);
        Assert.Null(menu.Problem(Platform));
        Assert.Contains("2 to 8", new RoseMenu([Option("Copy"), Folder("More", Option("A"))]).Problem(Platform));
        Assert.Contains("no action of its own",
            new RoseMenu([Option("Copy"), Folder("More", Option("A"), Option("B")) with { Action = Press("KeyV") }]).Problem(Platform));
        Assert.Contains("can't be a folder",
            new RoseMenu([Option("Copy"), Folder("More", Option("A"), Option("B")) with { PassThrough = true }]).Problem(Platform));
        Assert.Contains("taps keys",
            new RoseMenu([Option("Copy"), Folder("More", Option("A"), new RoseItem(new ButtonAction(ActionTypes.Hold, ["KeyC"])))]).Problem(Platform));
    }

    [Fact]
    public void AFolderIsSetAndItsActionsCount()
    {
        var folder = Folder("More", new RoseItem(new ButtonAction(ActionTypes.Macro, Macro: "m1")), Option("A"));
        Assert.True(folder.IsSet);
        var menu = new RoseMenu([folder, RoseItem.Empty]);
        Assert.Contains("m1", menu.AllActions.Select(a => a.Macro));
    }

    [Fact]
    public void AtWalksThePathOfFoldersAndStopsWhereItBreaks()
    {
        var items = new[] { Option("Copy"), Folder("More", Option("A"), Folder("Deeper", Option("B"), Option("C"))) };
        Assert.Equal("B", RoseMenu.At(items, [1, 1])![0].Label);
        Assert.Same(items, RoseMenu.At(items, []));
        Assert.Null(RoseMenu.At(items, [0]));
        Assert.Null(RoseMenu.At(items, [1, 7]));
    }

    [Fact]
    public void ASecondLayerFolderPassesThroughToTheMainLayersFolderInItsPlace()
    {
        var main = new RoseMenu([Option("Copy"), Folder("Apps", Option("Mail"), Option("Notes"))]);
        var second = new RoseMenu([
            new RoseItem(ButtonAction.Nothing, PassThrough: true),
            Folder("Apps", Option("Paint"), new RoseItem(ButtonAction.Nothing, PassThrough: true)),
        ]);
        var shown = second.Over(main);
        Assert.True(shown.Items[0].FromMain);
        Assert.Equal("Paint", shown.Items[1].Items![0].Label);
        Assert.Equal("Notes", shown.Items[1].Items![1].Label);
        Assert.True(shown.Items[1].Items![1].FromMain);

        // A whole folder passed through brings the main layer's options, marked as the main layer's.
        var passed = new RoseMenu([Option("Paste"), new RoseItem(ButtonAction.Nothing, PassThrough: true)]).Over(main);
        Assert.All(passed.Items[1].Items!, item => Assert.True(item.FromMain));
    }

    [Fact]
    public void TheRimOpensAFolderAndEnteringStartsInTheMiddle()
    {
        var tracker = new RoseTracker { Dpi = 1000 };
        tracker.Reset(4);
        tracker.Add(0, 0);
        tracker.Add(0, -Mm(8));
        Assert.Equal(0, tracker.Selected);
        Assert.False(tracker.AtRim);
        tracker.Add(0, -Mm(10));
        Assert.True(tracker.AtRim);

        tracker.Enter(3);
        Assert.Null(tracker.Selected);
        Assert.Equal(3, tracker.Count);
        Assert.Equal(0, tracker.X);
        Assert.True(tracker.LeftCenter);
        // The next movement counts at once: entering isn't a new press, so nothing is dropped as stale.
        Assert.True(tracker.Add(Mm(8), 0));
    }

    [Fact]
    public void FoldersNestDeeperThanTheUsualJsonLimit()
    {
        var item = Folder("Leaf", Option("A"), Option("B"));
        for (int i = 0; i < 60; i++)
            item = Folder($"F{i}", item, Option("X"));
        var menu = new RoseMenu([item, Option("Y")], "deep", "Deep");
        var json = JsonSerializer.Serialize(menu, Protocol.Json);
        var back = JsonSerializer.Deserialize<RoseMenu>(json, Protocol.Json)!;
        Assert.Null(back.Problem(Platform));
        using var document = JsonDocument.Parse(json, Protocol.Document);
    }

    [Fact]
    public void AButtonShowsItsMenuFromTheLibrary()
    {
        var menu = new RoseMenu([Option("Copy"), Option("Paste")], "edit", "Editing");
        var binding = new ButtonBinding("gesture", ButtonBinding.RoseMode, Press("KeyV"), Menu: "edit");
        Assert.Null(binding.Problem(Platform));
        var resolved = binding.Resolve(_ => null, id => id == "edit" ? menu : null);
        Assert.Same(menu, resolved.Rose);
        Assert.True(resolved.IsRose);

        var gone = binding.Resolve(_ => null, _ => null);
        Assert.False(gone.IsRose);
        Assert.Equal(["KeyV"], gone.Clicks.Single.Keys);
    }

    [Fact]
    public void RosesFromBeforeTheLibraryMoveIntoItFromBothLayers()
    {
        var rose = new RoseMenu([Option("Copy"), Option("Paste")]);
        var other = new RoseMenu([Option("Copy"), new RoseItem(ButtonAction.Nothing, PassThrough: true)]);
        var work = new Profile("work", "Work", [new ButtonBinding("gesture", ButtonBinding.RoseMode, Rose: rose)],
            Layer: new ProfileLayer([new ButtonBinding("gesture", ButtonBinding.RoseMode, Rose: other)]));
        var game = new Profile("game", "Game", [new ButtonBinding("middle", ButtonBinding.RoseMode, Rose: rose)]);
        var settings = new AgentSettings(Profiles: [work, game], ActiveProfile: "work").Migrated();

        Assert.Equal(2, settings.RoseMenus!.Count);
        var first = settings.AllProfiles[0].AllBindings[0];
        Assert.Null(first.Rose);
        Assert.Equal("Gesture button (Work)", settings.FindRose(first.Menu!)!.Name);
        Assert.Equal(first.Menu, settings.AllProfiles[1].AllBindings[0].Menu);
        var layer = settings.AllProfiles[0].SecondLayer.AllBindings[0];
        Assert.Equal("Gesture button (Work, second layer)", settings.FindRose(layer.Menu!)!.Name);
        Assert.Same(settings, settings.Migrated());

        var json = JsonSerializer.Serialize(settings, Protocol.Json);
        var back = JsonSerializer.Deserialize<AgentSettings>(json, Protocol.Json)!;
        Assert.Equal("Copy", back.Resolved(back.AllProfiles[0], false).Single().Rose!.Items[0].Label);
    }

    private static readonly ButtonBinding Copy = new("back", ButtonBinding.ActionMode, Press("ControlLeft", "KeyC"));
    private static readonly ButtonBinding Paste = new("back", ButtonBinding.ActionMode, Press("ControlLeft", "KeyV"));
    private static readonly ButtonBinding Follow = new("back", ButtonBinding.InheritMode);

    [Fact]
    public void AnInheritingButtonRunsTheBaseProfilesBindingInTheSameLayer()
    {
        var baseProfile = new Profile(Profile.DefaultId, "Default", [Copy], Layer: new ProfileLayer([Paste]));
        var work = new Profile("work", "Work", [Follow], Layer: new ProfileLayer([Follow]));
        var settings = new AgentSettings(Profiles: [baseProfile, work]);
        Assert.Null(Follow.Problem(Platform));

        Assert.Equal(["ControlLeft", "KeyC"], settings.Resolved(work, false).Single().Action!.Keys);
        Assert.Equal(["ControlLeft", "KeyV"], settings.Resolved(work, true).Single().Action!.Keys);
        // Without a second-layer binding of its own, the base profile's second layer runs its main one.
        var plain = settings with { Profiles = [baseProfile with { Layer = null }, work] };
        Assert.Equal(["ControlLeft", "KeyC"], plain.Resolved(work, true).Single().Action!.Keys);
    }

    [Fact]
    public void AnInheritingButtonIsUnboundWhereTheBaseProfileLeavesItAlone()
    {
        var settings = new AgentSettings(Profiles: [new Profile(Profile.DefaultId, "Default", []), new Profile("work", "Work", [Follow])]);
        Assert.Empty(settings.Resolved(settings.AllProfiles[1], false));
        // The base profile can't follow itself.
        var self = new AgentSettings(Profiles: [new Profile(Profile.DefaultId, "Default", [Follow])]);
        Assert.Empty(self.Resolved(self.AllProfiles[0], false));
    }

    [Fact]
    public void TheBaseProfileIsTheFirstOnceDefaultIsDeleted()
    {
        var settings = new AgentSettings(Profiles: [new Profile("home", "Home", [Copy]), new Profile("work", "Work", [Follow])]);
        Assert.Equal("home", settings.BaseProfile.Id);
        Assert.Equal(["ControlLeft", "KeyC"], settings.Resolved(settings.AllProfiles[1], false).Single().Action!.Keys);
    }

    [Fact]
    public void AnInheritingBindingCarriesNothingOfItsOwn()
    {
        Assert.Contains("nothing of its own", (Follow with { Action = Press("KeyC") }).Problem(Platform));
        var json = JsonSerializer.Serialize(Follow, Protocol.Json);
        Assert.Equal(ButtonBinding.InheritMode, JsonSerializer.Deserialize<ButtonBinding>(json, Protocol.Json)!.Mode);
    }
}
