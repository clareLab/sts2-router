using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace router;

internal static class Ui
{
    private static Theme? _theme;
    internal static Theme Theme => _theme ??= CreateTheme();
    internal static readonly Color[] RouteColors =
    [
        ModelDb.Character<Ironclad>().MapDrawingColor,
        ModelDb.Character<Silent>().MapDrawingColor,
        ModelDb.Character<Defect>().MapDrawingColor,
        ModelDb.Character<Necrobinder>().MapDrawingColor,
        ModelDb.Character<Regent>().MapDrawingColor
    ];
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

    internal static StyleBoxFlat Surface(string background, string border, int padding = 6) => new()
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

    internal static Texture2D? Texture(string path)
    {
        if (!ResourceLoader.Exists(path)) return null;
        var texture = GD.Load<Texture2D>(path);
        return texture is AtlasTexture atlas ? new AtlasTexture { Atlas = atlas.Atlas, Region = atlas.Region, FilterClip = true } : texture;
    }

    internal static Button Button(string text, string name, Action action, float width = 40)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            Theme = Theme,
            CustomMinimumSize = new Vector2(width, 38),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        button.Pressed += action;
        return button;
    }

    internal static Button Icon(string path, string tooltip, string name, Action action)
    {
        var button = Button("", name, action);
        button.TooltipText = tooltip;
        var icon = new TextureRect
        {
            Texture = Texture(path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        button.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, margin: 9);
        return button;
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
        var panel = Surface("1b2b35", "83918d");
        panel.ShadowColor = new Color("0b141c80");
        panel.ShadowSize = 3;
        panel.ShadowOffset = new Vector2(0, 2);
        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetStylebox("panel", "PopupMenu", Surface("1b2b35", "83918d", 8));
        theme.SetStylebox("hover", "PopupMenu", Surface("526575", "f2d68d", 6));
        theme.SetConstant("v_separation", "PopupMenu", 12);
        foreach (string type in new[] { "Button", "OptionButton", "LineEdit" })
        {
            theme.SetStylebox("normal", type, Surface("2e4351", "526c75"));
            theme.SetStylebox("hover", type, Surface("526575", "f2d68d"));
            theme.SetStylebox("pressed", type, Surface("15232d", "d1ac60"));
            theme.SetStylebox("hover_pressed", type, Surface("526575", "fff2cd"));
            theme.SetStylebox("disabled", type, Surface("23323a", "3c4c53"));
            theme.SetStylebox("focus", type, Surface("00000000", "f2d68d"));
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
