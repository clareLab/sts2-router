using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace router;

internal partial class RouterControl : Control
{
    private NMapScreen _screen = null!;
    private RouteInk? _ink;
    private Settings _settings = Settings.Defaults();
    private PanelContainer _toolbar = null!;
    private PanelContainer _editor = null!;
    private VBoxContainer _editorContent = null!;
    private readonly List<Button> _toggles = [];
    private Task<(int Group, RoutePlan Plan)[]>? _planning;
    private CancellationTokenSource? _cancellation;
    private MapSnapshot? _snapshot;
    private ActMap? _lastMap;
    private MapCoord? _lastCoord;
    private RunState? _lastRun;
    private bool _dirty = true;
    private bool _wasOpen;
    private bool _failed;
    private int _selected;
    private string _path = "";
    internal int CompletedPlans { get; private set; }
    internal Settings Configuration => _settings;
    internal PanelContainer Editor => _editor;

    internal void Initialize()
    {
        try
        {
            _screen = (NMapScreen)GetParent();
            _screen.Opened += Invalidate;
            TreeExiting += CleanUp;
            ((SceneTree)Engine.GetMainLoop()).ProcessFrame += Tick;
            Theme = Ui.Theme;
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _path = ProjectSettings.GlobalizePath("user://router/settings.json");
            try { if (File.Exists(_path)) _settings = Settings.Parse(File.ReadAllText(_path)); }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                GD.PrintErr("[router] Using default priorities: " + error.Message);
            }
            BuildToolbar();
            _editor = new PanelContainer { Name = "Editor", Theme = Ui.Theme, Visible = false, CustomMinimumSize = new Vector2(620, 0) };
            AddChild(_editor);
            _editorContent = new VBoxContainer();
            _editorContent.AddThemeConstantOverride("separation", 12);
            Ui.Padding(_editor, 8).AddChild(_editorContent);
            BuildEditor();
        }
        catch (Exception error) { Fail(error); }
    }

    internal void Invalidate() => _dirty = true;

    private void Tick()
    {
        if (_failed) return;
        try
        {
            bool open = _screen.IsVisibleInTree() && _screen.IsOpen && !_screen.IsTraveling;
            _toolbar.Visible = open;
            if (_ink != null && IsInstanceValid(_ink)) _ink.Visible = open;
            if (!open) { _editor.Hide(); _wasOpen = false; return; }
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run == null) return;
            if (!_wasOpen || !ReferenceEquals(_lastMap, run.Map) || !ReferenceEquals(_lastRun, run) || _lastCoord != run.CurrentMapCoord) _dirty = true;
            _wasOpen = true;
            if (_dirty)
            {
                _lastMap = run.Map;
                _lastRun = run;
                _lastCoord = run.CurrentMapCoord;
                BeginPlanning(run);
            }
            if (_planning is { IsCompleted: true })
            {
                var planning = _planning;
                _planning = null;
                if (planning.IsCompletedSuccessfully)
                {
                    EnsureInk();
                    _ink!.SetRoutes(_snapshot!, planning.Result);
                    CompletedPlans++;
                }
                else if (planning.Exception is { } error) throw error.GetBaseException();
            }
            var size = Size;
            float available = Math.Max(1, size.X - 32);
            float scale = Math.Min(1, Math.Min(available / 620, Math.Max(0.5f, (size.Y - 180) / 560)));
            _toolbar.Position = new Vector2(24, 144);
            _editor.Scale = new Vector2(scale, scale);
            _editor.Position = _toolbar.Position + new Vector2(0, _toolbar.Size.Y + 8);
        }
        catch (Exception error) { Fail(error); }
    }

    private void BeginPlanning(RunState run)
    {
        _dirty = false;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var snapshot = MapSnapshot.Capture(_screen, run);
        _snapshot = snapshot;
        EnsureInk();
        _ink!.SetRoutes(snapshot, []);
        var groups = _settings.Groups.Select((group, index) => (index, group.Enabled, Rules: group.Rules.ToArray())).Where(group => group.Enabled && group.Rules.Length > 0).ToArray();
        _planning = Task.Run(() => groups.Select(group => (group.index,
            Planner.Solve(snapshot.Nodes, snapshot.Starts, snapshot.Goals, group.Rules, snapshot.AfterChest, token))).ToArray(), token);
    }

    private void EnsureInk()
    {
        if (_ink != null && IsInstanceValid(_ink) && !_ink.IsQueuedForDeletion()) return;
        _ink = new RouteInk { Name = "RouterInk" };
        var points = _screen.GetNode<Control>("TheMap/Points");
        points.AddChild(_ink);
        points.MoveChild(_ink, 0);
    }

    private void CleanUp()
    {
        ((SceneTree)Engine.GetMainLoop()).ProcessFrame -= Tick;
        if (_screen != null && IsInstanceValid(_screen)) _screen.Opened -= Invalidate;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        if (_ink != null && IsInstanceValid(_ink)) _ink.QueueFree();
    }

    private void Fail(Exception error)
    {
        _failed = true;
        _cancellation?.Cancel();
        if (_ink != null && IsInstanceValid(_ink)) _ink.Hide();
        Hide();
        GD.PrintErr("[router] Map overlay disabled for this run: " + error.Message);
    }

    private void Changed()
    {
        _dirty = true;
        for (int i = 0; i < _toggles.Count; i++)
        {
            _toggles[i].SetPressedNoSignal(_settings.Groups[i].Enabled);
            _toggles[i].TooltipText = _settings.Groups[i].Name;
        }
        try { _settings.Save(_path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr("[router] Could not save priorities: " + error.Message);
            _toolbar.TooltipText = "Priorities could not be saved. Changes apply to this session.";
        }
    }

    private void BuildToolbar()
    {
        _toolbar = new PanelContainer { Name = "Toolbar", Theme = Ui.Theme };
        AddChild(_toolbar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        _toolbar.AddChild(row);
        var title = Ui.Text("Router", 22);
        title.CustomMinimumSize = new Vector2(76, 0);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        row.AddChild(title);
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            var button = GroupButton(index, "Toggle" + index, () =>
            {
                _settings.Groups[index].Enabled = !_settings.Groups[index].Enabled;
                Changed();
                if (_editor.Visible && _selected == index) BuildEditor();
            });
            button.ToggleMode = true;
            button.SetPressedNoSignal(_settings.Groups[i].Enabled);
            button.TooltipText = _settings.Groups[i].Name;
            row.AddChild(button);
            _toggles.Add(button);
        }
        row.AddChild(Ui.Icon(Ui.SettingsIcon, "Edit priorities", "Edit", () => _editor.Visible = !_editor.Visible));
    }

    private static Button GroupButton(int index, string name, Action action)
    {
        var button = Ui.Button((index + 1).ToString(), name, action, 38);
        var color = Ui.RouteColors[index].Lightened(0.18f);
        var style = Ui.Surface("23323a", color.ToHtml());
        style.BorderWidthBottom = 3;
        button.AddThemeStyleboxOverride("normal", style);
        var pressed = Ui.Surface(color.Darkened(0.42f).ToHtml(), color.Lightened(0.3f).ToHtml());
        pressed.BorderWidthBottom = 3;
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("hover_pressed", pressed);
        button.AddThemeColorOverride("font_color", color.Lightened(0.6f));
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        return button;
    }

    private void BuildEditor()
    {
        foreach (var child in _editorContent.GetChildren()) { _editorContent.RemoveChild(child); child.QueueFree(); }
        var group = _settings.Groups[_selected];
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        _editorContent.AddChild(tabs);
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            var tab = GroupButton(index, "Group" + i, () => { _selected = index; BuildEditor(); });
            tab.ToggleMode = true;
            tab.SetPressedNoSignal(i == _selected);
            tab.TooltipText = _settings.Groups[i].Name;
            tabs.AddChild(tab);
        }
        tabs.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        tabs.AddChild(Ui.Icon(Ui.CloseIcon, "Close", "Close", _editor.Hide));
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 8);
        _editorContent.AddChild(nameRow);
        var name = new LineEdit { Name = "GroupName", Text = group.Name, MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        name.TextChanged += text => { group.Name = string.IsNullOrWhiteSpace(text) ? $"Route {_selected + 1}" : text.Trim(); Changed(); };
        nameRow.AddChild(name);
        var enabled = Ui.Button(group.Enabled ? "Shown" : "Hidden", "Enabled", () => { group.Enabled = !group.Enabled; Changed(); BuildEditor(); }, 84);
        enabled.ToggleMode = true;
        enabled.SetPressedNoSignal(group.Enabled);
        nameRow.AddChild(enabled);
        _editorContent.AddChild(new HSeparator());
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        foreach (var (text, width) in new[] { ("", 22), ("Room", 128), ("Where", 144), ("Prefer", 100) })
        {
            var label = Ui.Text(text, 18);
            label.CustomMinimumSize = new Vector2(width, 0);
            header.AddChild(label);
        }
        _editorContent.AddChild(header);
        var scroll = new ScrollContainer
        {
            Name = "Priorities",
            CustomMinimumSize = new Vector2(0, Math.Clamp(group.Rules.Count, 1, 6) * 44),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        var rules = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rules.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(rules);
        _editorContent.AddChild(scroll);
        for (int i = 0; i < group.Rules.Count; i++) rules.AddChild(RuleRow(group, i));
        if (group.Rules.Count == 0)
        {
            var empty = Ui.Text("Add a priority to draw this route.", 18);
            empty.CustomMinimumSize = new Vector2(0, 38);
            rules.AddChild(empty);
        }
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        _editorContent.AddChild(footer);
        var add = Ui.Button("Add priority", "AddPriority", () =>
        {
            group.Rules.Add(new Rule(Room.RestSite, Segment.WholeAct, Preference.Most));
            Changed(); BuildEditor();
        }, 136);
        add.Disabled = group.Rules.Count >= Settings.MaxRules;
        footer.AddChild(add);
        footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var hint = Ui.Text("Top priority first", 16);
        footer.AddChild(hint);
        _editor.ResetSize();
    }

    private HBoxContainer RuleRow(RouteGroup group, int index)
    {
        var row = new HBoxContainer { Name = "Rule" + index };
        row.AddThemeConstantOverride("separation", 6);
        var number = Ui.Text((index + 1).ToString(), 18);
        number.CustomMinimumSize = new Vector2(22, 0);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        row.AddChild(number);
        var rule = group.Rules[index];
        var rooms = new[] { Room.Monster, Room.Elite, Room.RestSite, Room.Shop, Room.Unknown, Room.Treasure };
        var room = Select("Room", rooms.Select(Ui.RoomName).ToArray(), Array.IndexOf(rooms, rule.Room), 128,
            value => { group.Rules[index] = group.Rules[index] with { Room = rooms[value] }; Changed(); });
        row.AddChild(room);
        row.AddChild(Select("Where", ["Whole act", "Before chest", "After chest"], (int)rule.Segment, 144,
            value => { group.Rules[index] = group.Rules[index] with { Segment = (Segment)value }; Changed(); }));
        row.AddChild(Select("Prefer", ["Most", "Fewest", "Have", "Avoid"], (int)rule.Preference, 100,
            value => { group.Rules[index] = group.Rules[index] with { Preference = (Preference)value }; Changed(); }));
        var up = Ui.Icon("res://images/atlases/ui_atlas.sprites/settings_tiny_left_arrow.tres", "Move up", "Up", () => Move(group, index, -1));
        RotateArrow(up, Mathf.Pi / 2);
        up.Disabled = index == 0;
        up.CustomMinimumSize = new Vector2(32, 38);
        row.AddChild(up);
        var down = Ui.Icon("res://images/atlases/ui_atlas.sprites/settings_tiny_right_arrow.tres", "Move down", "Down", () => Move(group, index, 1));
        RotateArrow(down, Mathf.Pi / 2);
        down.Disabled = index == group.Rules.Count - 1;
        down.CustomMinimumSize = new Vector2(32, 38);
        row.AddChild(down);
        var remove = Ui.Icon(Ui.CloseIcon, "Remove priority", "Remove", () => { group.Rules.RemoveAt(index); Changed(); BuildEditor(); });
        remove.CustomMinimumSize = new Vector2(32, 38);
        row.AddChild(remove);
        return row;
    }

    private static void RotateArrow(Button button, float rotation)
    {
        var icon = button.GetChildren().OfType<TextureRect>().Single();
        icon.Rotation = rotation;
        icon.Resized += () => icon.PivotOffset = icon.Size * 0.5f;
    }

    private void Move(RouteGroup group, int index, int offset)
    {
        (group.Rules[index], group.Rules[index + offset]) = (group.Rules[index + offset], group.Rules[index]);
        Changed(); BuildEditor();
    }

    private static OptionButton Select(string name, string[] items, int selected, float width, Action<int> changed)
    {
        var select = new OptionButton
        {
            Name = name,
            Theme = Ui.Theme,
            CustomMinimumSize = new Vector2(width, 38),
            FitToLongestItem = false,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        select.AddThemeFontSizeOverride("font_size", 18);
        foreach (string item in items) select.AddItem(item);
        select.Selected = Math.Max(0, selected);
        select.ItemSelected += value => changed((int)value);
        return select;
    }
}
