using Godot;
using MegaCrit.Sts2.Core.Models;

namespace router;

internal static class Ui
{
    private static Theme? _theme;
    internal static Theme Theme => _theme ??= CreateTheme();
    internal const int ControlSize = 36;
    internal const int Gap = 6;
    internal const int ContentInset = 8;
    internal const int RoomWidth = 56;
    internal const int SegmentWidth = 152;
    internal const int PreferenceWidth = 104;
    internal const int EditorWidth = 488;
    internal static readonly CharacterModel[] Characters = ModelDb.AllCharacters.ToArray();
    internal static readonly Color[] RouteColors = Characters.Select(character => character.MapDrawingColor).ToArray();
    internal const string SettingsIcon = "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_settings.tres";
    internal const string CloseIcon = "res://images/atlases/compressed.sprites/back_button_x.tres";

    internal static Label Text(string text, int size = 20) => new()
    {
        Text = text,
        Theme = Theme,
        LabelSettings = new LabelSettings { Font = Theme.DefaultFont, FontSize = size, FontColor = new Color("eee5cf") },
        VerticalAlignment = VerticalAlignment.Center,
        MouseFilter = Control.MouseFilterEnum.Ignore
    };

    internal static StyleBoxFlat Surface(string background, string border, int padding = ContentInset) => new()
    {
        BgColor = new Color(background),
        BorderColor = new Color(border),
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        CornerDetail = 8,
        ContentMarginLeft = padding,
        ContentMarginRight = padding,
        ContentMarginTop = padding,
        ContentMarginBottom = padding
    };

    internal static StyleBoxFlat ButtonSurface(string background, string border)
    {
        var style = Surface(background, border);
        style.ContentMarginTop = 4;
        style.ContentMarginBottom = 4;
        return style;
    }

    internal static Texture2D? Texture(string path)
    {
        if (!ResourceLoader.Exists(path)) return null;
        var texture = GD.Load<Texture2D>(path);
        return texture is AtlasTexture atlas ? new AtlasTexture { Atlas = atlas.Atlas, Region = atlas.Region, FilterClip = true } : texture;
    }

    internal static Button Button(string text, string name, Action action, float width = ControlSize)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            Theme = Theme,
            CustomMinimumSize = new Vector2(width, ControlSize),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        button.Pressed += action;
        return button;
    }

    internal static Button Icon(string path, string tooltip, string name, Action action, float rotation = 0) =>
        Icon(Texture(path), tooltip, name, action, rotation);

    internal static Button Icon(Texture2D? texture, string tooltip, string name, Action action, float rotation = 0)
    {
        var button = Button("", name, action);
        button.TooltipText = tooltip;
        var icon = new TextureRect
        {
            Name = "Icon",
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Rotation = rotation
        };
        button.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, margin: ContentInset);
        icon.Resized += () => icon.PivotOffset = icon.Size * 0.5f;
        return button;
    }

    internal static void Disable(Button button, bool disabled)
    {
        button.Disabled = disabled;
        button.GetNode<TextureRect>("Icon").Modulate = new Color(1, 1, 1, disabled ? 0.3f : 1);
    }

    internal static Texture2D? RoomIcon(Room room) => Texture("res://images/atlases/ui_atlas.sprites/map/icons/map_" + (room switch
    {
        Room.Monster => "monster",
        Room.Elite => "elite",
        Room.RestSite => "rest",
        Room.Shop => "shop",
        Room.Treasure => "chest",
        _ => "unknown"
    }) + ".tres");

    internal static Control Heading(string text, int width)
    {
        var margin = new MarginContainer { Name = text + "Heading", CustomMinimumSize = new Vector2(width, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", ContentInset);
        var label = Text(text, 18);
        label.Name = "Text";
        margin.AddChild(label);
        return margin;
    }

    internal static MarginContainer Padding(Control parent, int padding)
    {
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, padding);
        parent.AddChild(margin);
        return margin;
    }

    internal static string RoomName(Room room) => room switch
    {
        Room.Monster => "Combat",
        Room.RestSite => "Rest site",
        Room.Unknown => "Unknown",
        _ => room.ToString()
    };

    private static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFontSize = 20, DefaultFont = GD.Load<Font>("res://themes/kreon_regular_shared.tres") };
        var panel = Surface("1b2b35", "83918d", 6);
        panel.ShadowColor = new Color("0b141c80");
        panel.ShadowSize = 3;
        panel.ShadowOffset = new Vector2(0, 2);
        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetStylebox("panel", "PopupMenu", Surface("1b2b35", "83918d", 8));
        theme.SetStylebox("hover", "PopupMenu", Surface("526575", "f2d68d", 6));
        theme.SetConstant("v_separation", "PopupMenu", 12);
        foreach (string type in new[] { "Button", "OptionButton" })
        {
            theme.SetStylebox("normal", type, ButtonSurface("2e4351", "526c75"));
            theme.SetStylebox("hover", type, ButtonSurface("526575", "f2d68d"));
            theme.SetStylebox("pressed", type, ButtonSurface("15232d", "d1ac60"));
            theme.SetStylebox("hover_pressed", type, ButtonSurface("526575", "fff2cd"));
            theme.SetStylebox("disabled", type, ButtonSurface("23323a", "3c4c53"));
            theme.SetStylebox("focus", type, ButtonSurface("00000000", "f2d68d"));
            theme.SetColor("font_color", type, new Color("eee5cf"));
            theme.SetColor("font_hover_color", type, new Color("fff2cd"));
            theme.SetColor("font_pressed_color", type, new Color("f2d68d"));
            theme.SetColor("font_disabled_color", type, new Color("83918d"));
        }
        theme.SetColor("font_color", "PopupMenu", new Color("eee5cf"));
        theme.SetColor("font_hover_color", "PopupMenu", new Color("fff2cd"));
        theme.SetStylebox("panel", "TooltipPanel", Surface("1b2b35", "83918d", 10));
        theme.SetColor("font_color", "TooltipLabel", new Color("eee5cf"));
        theme.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = new Color("405662"), Thickness = 1 });
        return theme;
    }
}
