using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace router;

internal partial class RouterControl : Control
{
    private NMapScreen _screen = null!;
    private readonly RouteTint _tint = new();
    private Settings _settings = Settings.Defaults();
    private PanelContainer _toolbar = null!;
    private PanelContainer _editor = null!;
    private Button _editButton = null!;
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
            _editor = new PanelContainer { Name = "Editor", Theme = Ui.Theme, Visible = false, CustomMinimumSize = new Vector2(Ui.EditorWidth, 0) };
            AddChild(_editor);
            _editor.VisibilityChanged += UpdateSwatches;
            _editorContent = new VBoxContainer();
            _editorContent.AddThemeConstantOverride("separation", 8);
            Ui.Padding(_editor, 6).AddChild(_editorContent);
            BuildEditor();
            UpdateSwatches();
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
            if (!open) { _editor.Hide(); if (_wasOpen) _tint.Clear(); _wasOpen = false; return; }
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
                    _tint.Apply(_screen, _snapshot!, planning.Result);
                    CompletedPlans++;
                }
                else if (planning.Exception is { } error) throw error.GetBaseException();
            }
            var size = Size;
            float available = Math.Max(1, size.X - 32);
            float scale = Math.Min(1, Math.Min(available / Ui.EditorWidth, Math.Max(0.5f, (size.Y - 180) / 560)));
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
        var snapshot = MapSnapshot.Capture(run);
        _snapshot = snapshot;
        _tint.Clear();
        var groups = _settings.Groups.Select((group, index) => (index, group.Enabled, Rules: group.Rules.ToArray())).Where(group => group.Enabled && group.Rules.Length > 0).ToArray();
        _planning = Task.Run(() => groups.Select(group => (group.index,
            Planner.Solve(snapshot.Nodes, snapshot.Starts, snapshot.Goals, group.Rules, snapshot.AfterChest, token))).ToArray(), token);
    }

    private void CleanUp()
    {
        ((SceneTree)Engine.GetMainLoop()).ProcessFrame -= Tick;
        if (_screen != null && IsInstanceValid(_screen)) _screen.Opened -= Invalidate;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _tint.Clear();
    }

    private void Fail(Exception error)
    {
        _failed = true;
        _cancellation?.Cancel();
        _tint.Clear();
        Hide();
        GD.PrintErr("[router] Map overlay disabled for this run: " + error.Message);
    }

    private void Changed()
    {
        _dirty = true;
        UpdateSwatches();
        try { _settings.Save(_path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr("[router] Could not save priorities: " + error.Message);
            _toolbar.TooltipText = "Priorities could not be saved. Changes apply to this session.";
        }
    }

    private void UpdateSwatches()
    {
        bool editing = _editor.Visible;
        _editButton.SetPressedNoSignal(editing);
        for (int i = 0; i < _toggles.Count; i++)
        {
            _toggles[i].SetPressedNoSignal(editing ? i == _selected : _settings.Groups[i].Enabled);
            _toggles[i].TooltipText = editing ? "Edit priorities" : _settings.Groups[i].Enabled ? "Hide route" : "Show route";
        }
    }

    private void BuildToolbar()
    {
        _toolbar = new PanelContainer { Name = "Toolbar", Theme = Ui.Theme };
        AddChild(_toolbar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap);
        Ui.Padding(_toolbar, 6).AddChild(row);
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            var button = GroupButton(index, "Toggle" + index, () =>
            {
                _selected = index;
                if (!_editor.Visible)
                {
                    _settings.Groups[index].Enabled = !_settings.Groups[index].Enabled;
                    Changed();
                }
                BuildEditor();
                UpdateSwatches();
            });
            button.ToggleMode = true;
            button.SetPressedNoSignal(_settings.Groups[i].Enabled);
            button.TooltipText = _settings.Groups[i].Enabled ? "Hide route" : "Show route";
            row.AddChild(button);
            _toggles.Add(button);
        }
        _editButton = Ui.Icon(Ui.SettingsIcon, "Edit priorities", "Edit", () => _editor.Visible = !_editor.Visible);
        _editButton.ToggleMode = true;
        row.AddChild(_editButton);
    }

    private static Button GroupButton(int index, string name, Action action)
    {
        var button = Ui.Icon(Ui.Characters[index].IconTexture, "", name, action);
        var color = Ui.RouteColors[index];
        button.AddThemeStyleboxOverride("normal", Ui.ButtonSurface("23323a", color.Darkened(0.15f).ToHtml()));
        button.AddThemeStyleboxOverride("hover", Ui.ButtonSurface(color.Darkened(0.3f).ToHtml(), "f2d68d"));
        button.AddThemeStyleboxOverride("pressed", Ui.ButtonSurface(color.Darkened(0.35f).ToHtml(), color.Lightened(0.65f).ToHtml()));
        button.AddThemeStyleboxOverride("hover_pressed", Ui.ButtonSurface(color.Darkened(0.15f).ToHtml(), "fff2cd"));
        return button;
    }

    private void BuildEditor()
    {
        foreach (var child in _editorContent.GetChildren()) { _editorContent.RemoveChild(child); child.QueueFree(); }
        var group = _settings.Groups[_selected];
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", Ui.Gap);
        header.AddChild(Ui.Heading("Room", Ui.RoomWidth));
        header.AddChild(Ui.Heading("Where", Ui.SegmentWidth));
        header.AddChild(Ui.Heading("Prefer", Ui.PreferenceWidth));
        _editorContent.AddChild(header);
        var scroll = new ScrollContainer
        {
            Name = "Priorities",
            CustomMinimumSize = new Vector2(0, Math.Clamp(group.Rules.Count, 1, 6) * (Ui.ControlSize + Ui.Gap) - Ui.Gap),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        var rules = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rules.AddThemeConstantOverride("separation", Ui.Gap);
        scroll.AddChild(rules);
        _editorContent.AddChild(scroll);
        for (int i = 0; i < group.Rules.Count; i++) rules.AddChild(RuleRow(group, i));
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        _editorContent.AddChild(footer);
        var add = Ui.Icon(Ui.CloseIcon, "Add priority", "AddPriority", () =>
        {
            group.Rules.Add(new Rule(Room.RestSite, Segment.WholeAct, Preference.Most));
            Changed(); BuildEditor();
        }, Mathf.Pi / 4);
        Ui.Disable(add, group.Rules.Count >= Settings.MaxRules);
        footer.AddChild(add);
        var enabled = Ui.Icon("res://images/atlases/ui_atlas.sprites/checkbox_" + (group.Enabled ? "ticked" : "unticked") + ".tres",
            group.Enabled ? "Hide route" : "Show route", "Enabled", () =>
            {
                group.Enabled = !group.Enabled;
                Changed();
                BuildEditor();
            });
        footer.AddChild(enabled);
        _editor.ResetSize();
    }

    private HBoxContainer RuleRow(RouteGroup group, int index)
    {
        var row = new HBoxContainer { Name = "Rule" + index };
        row.AddThemeConstantOverride("separation", Ui.Gap);
        var rule = group.Rules[index];
        var rooms = new[] { Room.Monster, Room.Elite, Room.RestSite, Room.Shop, Room.Unknown, Room.Treasure };
        var room = Select("Room", rooms.Select(Ui.RoomName).ToArray(), Array.IndexOf(rooms, rule.Room), Ui.RoomWidth,
            value => { group.Rules[index] = group.Rules[index] with { Room = rooms[value] }; Changed(); });
        room.ExpandIcon = true;
        room.IconAlignment = HorizontalAlignment.Left;
        room.AddThemeConstantOverride("icon_max_width", 22);
        for (int i = 0; i < rooms.Length; i++)
        {
            room.SetItemIcon(i, Ui.RoomIcon(rooms[i]));
            room.GetPopup().SetItemIconMaxWidth(i, 22);
        }
        void UpdateRoomIcon()
        {
            room.Text = "";
            room.TooltipText = Ui.RoomName(rooms[room.Selected]);
        }
        room.ItemSelected += _ => UpdateRoomIcon();
        UpdateRoomIcon();
        row.AddChild(room);
        row.AddChild(Select("Where", ["Whole act", "Before chest", "After chest"], (int)rule.Segment, Ui.SegmentWidth,
            value => { group.Rules[index] = group.Rules[index] with { Segment = (Segment)value }; Changed(); }));
        row.AddChild(Select("Prefer", ["Most", "Fewest", "Have", "Avoid"], (int)rule.Preference, Ui.PreferenceWidth,
            value => { group.Rules[index] = group.Rules[index] with { Preference = (Preference)value }; Changed(); }));
        var up = Ui.Icon("res://images/atlases/ui_atlas.sprites/settings_tiny_left_arrow.tres", "Move up", "Up", () => Move(group, index, -1), Mathf.Pi / 2);
        Ui.Disable(up, index == 0);
        row.AddChild(up);
        var down = Ui.Icon("res://images/atlases/ui_atlas.sprites/settings_tiny_right_arrow.tres", "Move down", "Down", () => Move(group, index, 1), Mathf.Pi / 2);
        Ui.Disable(down, index == group.Rules.Count - 1);
        row.AddChild(down);
        var remove = Ui.Icon(Ui.CloseIcon, "Remove priority", "Remove", () => { group.Rules.RemoveAt(index); Changed(); BuildEditor(); });
        row.AddChild(remove);
        return row;
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
            CustomMinimumSize = new Vector2(width, Ui.ControlSize),
            FitToLongestItem = false,
            Alignment = HorizontalAlignment.Left,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        select.AddThemeFontSizeOverride("font_size", 18);
        foreach (string item in items) select.AddItem(item);
        select.Selected = Math.Max(0, selected);
        select.ItemSelected += value => changed((int)value);
        return select;
    }
}
