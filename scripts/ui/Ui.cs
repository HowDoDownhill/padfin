using System.Collections.Generic;
using Godot;

namespace Padfin;

/// <summary>
/// Builders for controls made in code. The look comes from RomBox: its theme resource and type
/// variations, frosted mica panels, controller glyphs, and the Appearance palette colours.
/// </summary>
public static class Ui
{
    private const string MicaMaterialPath = "res://assets/materials/mica_panel.tres";

    /// <summary>Left and right content margin, in 1080p units.</summary>
    public const int Margin = 60;

    /// <summary>Gap between the window edge and a panel, as in RomBox.</summary>
    public const int PanelGap = 10;

    public static Color Text => Appearance.Text;
    public static Color TextDim => Appearance.CaptionText;
    public static Color Accent => Appearance.Accent;
    public static Color AccentDim => new(Appearance.Accent, 0.35f);
    public static Color Surface => Appearance.Overlay(0.07f, 0.06f);
    public static Color SurfaceHi => Appearance.Overlay(0.14f, 0.1f);

    public static ShaderMaterial Mica => GD.Load<ShaderMaterial>(MicaMaterialPath);

    public static StyleBoxFlat Box(Color bg, int radius = 10, int border = 0, Color? borderColor = null, int padX = 0, int padY = 0, int expand = 0)
    {
        var box = new StyleBoxFlat { BgColor = bg, BorderColor = borderColor ?? Colors.White, AntiAliasing = true };
        box.SetCornerRadiusAll(radius);
        box.SetBorderWidthAll(border);
        box.SetExpandMarginAll(expand);
        box.ContentMarginLeft = padX;
        box.ContentMarginRight = padX;
        box.ContentMarginTop = padY;
        box.ContentMarginBottom = padY;
        return box;
    }

    /// <param name="variation">A theme type from app_theme.tres: DisplayLabel, TitleLabel, CaptionLabel, MutedLabel, AccentLabel, WarningLabel.</param>
    public static Label Label(string text, int size = 0, Color? color = null, string variation = null)
    {
        var label = new Label { Text = text ?? "", MouseFilter = Control.MouseFilterEnum.Ignore };
        if (variation != null) { label.ThemeTypeVariation = variation; }
        if (size > 0) { label.AddThemeFontSizeOverride("font_size", size); }
        if (color != null) { label.AddThemeColorOverride("font_color", color.Value); }
        return label;
    }

    /// <summary>A RomBox list button: no box of its own, marked by the gliding selection highlight.</summary>
    public static Button Button(string text, System.Action pressed = null)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.All, ThemeTypeVariation = "ListButton" };
        if (pressed != null) { button.Pressed += pressed; }
        return button;
    }

    /// <summary>A list button with a faint box, for actions that sit directly on the background.</summary>
    public static Button ActionButton(string text, System.Action pressed = null)
    {
        var button = Button(text, pressed);
        button.AddThemeStyleboxOverride("normal", Box(Surface, 10, padX: 18, padY: 8));
        button.AddThemeStyleboxOverride("hover", Box(SurfaceHi, 10, padX: 18, padY: 8));
        return button;
    }

    /// <summary>A footer prompt: the glyph for an input action beside its label. Clicking it performs the action.</summary>
    public static Button Hint(string action, string label, int fontSize = 0)
    {
        var button = new Button
        {
            Text = label,
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "FlatButton",
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }
        if (fontSize > 0) { button.AddThemeFontSizeOverride("font_size", fontSize); }
        ControllerGlyph.Apply(button, action);
        button.Pressed += () =>
        {
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        };
        return button;
    }

    /// <summary>A frosted panel with RomBox's rounded corners and drop shadow.</summary>
    public static PanelContainer MicaPanel(Control content = null, int padX = 24, int padY = 16)
    {
        var panel = new PanelContainer { Material = Mica };
        panel.AddThemeStyleboxOverride("panel", Box(Colors.Black, 12));
        MicaShadow.Attach(panel, MicaShadow.DefaultColor);
        if (content != null) { panel.AddChild(Pad(content, padX, padY, padX, padY)); }
        return panel;
    }

    /// <summary>The header bar at the top of a screen.</summary>
    public static Control Header(string title, params Control[] right)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 28);
        row.AddChild(Label(title, variation: "DisplayLabel"));
        row.AddChild(Spacer(expand: true));
        foreach (var control in right) { row.AddChild(control); }
        return Pad(MicaPanel(row, 28, 10), PanelGap, PanelGap, PanelGap, 0);
    }

    public static Control Spacer(int width = 0, int height = 0, bool expand = false)
    {
        var spacer = new Control { CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (expand)
        {
            spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        }
        return spacer;
    }

    public static MarginContainer Pad(Control child, int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        if (child != null) { margin.AddChild(child); }
        return margin;
    }

    public static void Fill(Control control) => control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

    /// <summary>
    /// Keeps directional focus inside a vertical list: up from the first and down from the last wrap around,
    /// left and right stay put. Used by modal menus so focus cannot reach the screen underneath.
    /// </summary>
    public static void ConfineVertical(IReadOnlyList<Control> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var self = item.GetPath();
            item.FocusNeighborLeft = self;
            item.FocusNeighborRight = self;
            item.FocusNeighborTop = items[(i + items.Count - 1) % items.Count].GetPath();
            item.FocusNeighborBottom = items[(i + 1) % items.Count].GetPath();
            item.FocusPrevious = item.FocusNeighborTop;
            item.FocusNext = item.FocusNeighborBottom;
        }
    }

    public static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) { seconds = 0; }
        var span = System.TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
    }
}
