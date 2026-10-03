using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace router;

internal static class SelfTests
{
    private static readonly List<string> Passed = [];

    internal static void Initialize()
    {
        if (!OS.GetCmdlineArgs().Contains("--router-selftest")) return;
        ((SceneTree)Engine.GetMainLoop()).ProcessFrame += Start;
    }

    private static void Start()
    {
        if (NGame.Instance?.MainMenu == null || !SaveManager.Instance.IsProfileInitialized) return;
        ((SceneTree)Engine.GetMainLoop()).ProcessFrame -= Start;
        _ = Run();
    }

    private static async Task Run()
    {
        string? error = null;
        try
        {
            if (!File.Exists(ProjectSettings.GlobalizePath("user://.router-test-sandbox"))) throw new InvalidOperationException("An isolated test sandbox is required.");
            string settingsPath = ProjectSettings.GlobalizePath("user://router/settings.json");
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            await Frames(3);
            SaveManager.Instance.SetFtuesEnabled(false);
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
                ActModel.GetDefaultList(), [], "ROUTER-PREVIEW-001", GameMode.Standard, 10);
            var screen = NMapScreen.Instance!;
            screen.Open(true);
            var tree = (SceneTree)Engine.GetMainLoop();
            await tree.ToSignal(tree.CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            var router = screen.GetNode<RouterControl>("Router");
            Check(router.GetNodeOrNull<Control>("Toolbar") != null, "map controls initialise");
            await Planned(router, 0);
            Check(router.IsVisibleInTree(), "overlay attaches to the native map");
            Check(router.Configuration.Groups.Count(g => g.Enabled) == 2, "two presets enabled");
            var before = Graph(run.Map);
            var snapshot = MapSnapshot.Capture(screen, run);
            Check(snapshot.Nodes.Count > 20 && snapshot.Starts.Length > 0, "native map graph captured");
            var pointContainer = screen.GetNode<Control>("TheMap/Points");
            var pointNodes = pointContainer.GetChildren().OfType<NMapPoint>().Where(p => !p.IsQueuedForDeletion()).ToArray();
            for (int i = 0; i < pointNodes.Length; i++)
            {
                if (pointNodes[i] is not NNormalMapPoint normal) continue;
                var icon = normal.GetNode<Control>("%Icon");
                var centre = icon.GetGlobalTransform() * (icon.Size * 0.5f);
                Check(centre.DistanceTo(pointContainer.GetGlobalTransform() * snapshot.Positions[i]) < 2, "route ring aligns with room icon " + i);
            }
            foreach (var group in router.Configuration.Groups.Take(2))
            {
                var plan = Planner.Solve(snapshot.Nodes, snapshot.Starts, snapshot.Goals, group.Rules, snapshot.AfterChest);
                Check(plan.Nodes.Count >= 10 && plan.Edges.Count >= 9, group.Name + " reaches the boss");
            }
            var ink = screen.GetNode<RouteInk>("TheMap/Points/RouterInk");
            Check(ink.Visible && ink.GetIndex() == 0, "route colours render beneath native node icons");
            await Screenshot("map");
            var toggle = Descendants(router).OfType<Button>().Single(b => b.Name == "Toggle0");
            int plans = router.CompletedPlans;
            await Click(toggle);
            Check(!router.Configuration.Groups[0].Enabled, "toolbar group toggle handles mouse input");
            await Planned(router, plans);
            await Click(toggle);
            var edit = Descendants(router).OfType<Button>().Single(b => b.Name == "Edit");
            await Click(edit);
            Check(router.Editor.Visible, "map settings open with mouse input");
            await Frames(4);
            Check(router.Editor.GetGlobalRect().End.Y <= router.Size.Y && router.Editor.GetGlobalRect().End.X <= router.Size.X, "editor fits in the viewport");
            CheckLayout(router);
            await Screenshot("editor");
            var first = router.Configuration.Groups[0].Rules[0];
            var down = router.Editor.FindChild("Rule0", true, false)!.GetNode<Button>("Down");
            await Click(down);
            Check(router.Configuration.Groups[0].Rules[1] == first, "priority order changes through native buttons");
            var up = router.Editor.FindChild("Rule1", true, false)!.GetNode<Button>("Up");
            await Click(up);
            var option = router.Editor.FindChild("Rule0", true, false)!.GetNode<OptionButton>("Where");
            await Click(option);
            Check(option.GetPopup().Visible, "native dropdown opens");
            option.GetPopup().Hide();
            option.Select(2);
            option.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            Check(router.Configuration.Groups[0].Rules[0].Segment == Segment.AfterChest, "scope edit updates the plan");
            option.Select(0);
            option.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            var third = Descendants(router.Editor).OfType<Button>().Single(b => b.Name == "Group2");
            await Click(third);
            await Click(Descendants(router.Editor).OfType<Button>().Single(b => b.Name == "AddPriority"));
            Check(router.Configuration.Groups[2].Rules.Count == 1, "empty group accepts a new priority");
            await Click(router.Editor.FindChild("Rule0", true, false)!.GetNode<Button>("Remove"));
            Check(router.Configuration.Groups[2].Rules.Count == 0, "priority removal keeps an empty group valid");
            var saved = Settings.Parse(File.ReadAllText(ProjectSettings.GlobalizePath("user://router/settings.json")));
            Check(saved.Groups[0].Rules.SequenceEqual(router.Configuration.Groups[0].Rules), "UI edits persist");
            Check(Graph(run.Map) == before, "planning and settings do not mutate the map");
            screen.Close(false);
            await Frames(3);
            Check(!router.IsVisibleInTree(), "overlay hides with the map");
            screen.Open(true);
            await Frames(3);
            router.Editor.Hide();
            plans = router.CompletedPlans;
            var point = run.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Treasure);
            run.AddVisitedMapCoord(point.coord);
            await Planned(router, plans);
            var moved = MapSnapshot.Capture(screen, run);
            Check(moved.AfterChest && moved.Current != snapshot.Current, "room progress updates chest phase and origin");
            var future = Planner.Solve(moved.Nodes, moved.Starts, moved.Goals, router.Configuration.Groups[1].Rules, moved.AfterChest);
            Check(!future.Nodes.Contains(moved.Current!.Value), "current room is excluded from future scoring");
            Check(future.Nodes.All(id => moved.Positions[id].Y < moved.Positions[moved.Current.Value].Y), "route contains only future rooms");
            plans = router.CompletedPlans;
            screen.SetMap(run.Map, run.Rng.Seed, false);
            await Planned(router, plans);
            Check(screen.GetNode<Control>("TheMap/Points").GetChildren().OfType<RouteInk>().Count() == 1, "map rebuild replaces the overlay without duplication");
            for (int act = 1; act < run.Acts.Count; act++)
            {
                plans = router.CompletedPlans;
                await RunManager.Instance.SetActInternal(act);
                screen.Open(true);
                await Frames(4);
                await Planned(router, plans);
                var nextAct = MapSnapshot.Capture(screen, run);
                var route = Planner.Solve(nextAct.Nodes, nextAct.Starts, nextAct.Goals, router.Configuration.Groups[0].Rules, nextAct.AfterChest);
                Check(!nextAct.AfterChest && route.Nodes.IsSupersetOf(nextAct.Goals), $"act {act + 1} starts afresh and reaches its final boss");
                if (act == 2) Check(run.Map.SecondBossMapPoint != null && route.Nodes.Count(id => nextAct.Nodes[id].Room == Room.Boss) == 2, "A10 route includes both bosses");
            }
            Check(SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant, "planning preserves game speed");
            GD.Print("[router] SELFTEST_OK");
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            GD.PrintErr("[router] SELFTEST_FAILED " + error);
        }
        finally
        {
            File.WriteAllText(ProjectSettings.GlobalizePath("user://router-selftest.json"), JsonSerializer.Serialize(new { success = error == null, passed = Passed, error }));
            ((SceneTree)Engine.GetMainLoop()).Quit(error == null ? 0 : 1);
        }
    }

    private static void CheckLayout(RouterControl router)
    {
        var rows = Descendants(router.Editor).OfType<HBoxContainer>().Where(r => r.Name.ToString().StartsWith("Rule", StringComparison.Ordinal)).ToArray();
        Check(rows.Length == 4, "all preset priorities visible");
        foreach (var row in rows)
        {
            var children = row.GetChildren().OfType<Control>().ToArray();
            Check(children.Zip(children.Skip(1), (a, b) => a.GetGlobalRect().End.X <= b.GetGlobalRect().Position.X + 1).All(v => v), row.Name + " columns do not overlap");
            foreach (var option in children.OfType<OptionButton>())
            {
                float width = option.GetThemeFont("font").GetStringSize(option.Text, fontSize: option.GetThemeFontSize("font_size")).X;
                Check(width + 22 < option.Size.X, row.Name + " " + option.Name + " label fits");
            }
        }
    }

    private static string Graph(ActMap map) => string.Join(';', map.GetAllMapPoints().Select(p => p.coord + ":" + p.PointType + ":" + string.Join(',', p.Children.Select(c => c.coord))));

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static async Task Click(Control control)
    {
        var point = control.GetGlobalRect().GetCenter();
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        root.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool pressed in new[] { true, false })
        {
            root.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Left : 0 }, true);
            await Frames(2);
        }
    }

    private static async Task Planned(RouterControl router, int previous)
    {
        for (int i = 0; i < 600 && router.CompletedPlans <= previous; i++) await Frames(1);
        Check(router.CompletedPlans > previous, "background planning completes");
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        Passed.Add(name);
        GD.Print("[router] PASS " + name);
    }

    private static async Task Frames(int count)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (int i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static async Task Screenshot(string name)
    {
        await Frames(5);
        Check(NGame.Instance!.GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"user://router-{name}.png")) == Error.Ok, name + " screenshot");
    }
}
