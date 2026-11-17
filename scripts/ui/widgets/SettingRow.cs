using System;
using System.Collections.Generic;
using Godot;

namespace Padfin;

/// <summary>
/// One line in the settings list, after RomBox's settings entry: a label on the left and, on the
/// right, either a carousel of choices or a value with an action. Left and Right step a carousel,
/// A steps it forward or runs the action.
/// </summary>
public partial class SettingRow : MarginContainer
{
    private const int WidgetWidth = 480;

    /// <summary>A sentence shown under the list while this row has focus.</summary>
    public string Help { get; private set; }

    private PanelContainer _panel;
    private CarouselButton _carousel;
    private Label _value;
    private Action _pressed;

    public SettingRow() { }

    private SettingRow(string label, string help, Control widget)
    {
        Help = help;
        FocusMode = FocusModeEnum.All;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" }) { AddThemeConstantOverride(side, 5); }

        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", FocusHighlight.CreateStyle(14, 10));
        AddChild(_panel);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 20);
        var name = Ui.Label(label);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        row.AddChild(name);
        widget.CustomMinimumSize = new Vector2(WidgetWidth, 0);
        widget.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        widget.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(widget);
        _panel.AddChild(row);

        FocusEntered += () => FocusHighlight.Set(this, _panel, true);
        FocusExited += () => FocusHighlight.Set(this, _panel, false);
    }

    /// <summary>A setting with a fixed set of choices.</summary>
    public static SettingRow Choice(string label, string help, IReadOnlyList<string> options, int selected, Action<int> changed)
    {
        var carousel = new CarouselButton();
        foreach (var option in options) { carousel.AddItem(option); }
        carousel.Select(Math.Clamp(selected, 0, Math.Max(0, options.Count - 1)));
        carousel.ItemSelected += index => changed((int)index);
        return new SettingRow(label, help, carousel) { _carousel = carousel };
    }

    /// <summary>A line that shows a value and does something when chosen. A null action makes it read-only.</summary>
    public static SettingRow Button(string label, string help, string value, Action pressed)
    {
        var text = Ui.Label(value, variation: "CaptionLabel", size: 24);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.ClipText = true;
        return new SettingRow(label, help, text) { _value = text, _pressed = pressed };
    }

    /// <summary>True when Left and Right change this row's value, so they must not move focus.</summary>
    public bool HasChoices => _carousel != null;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            GrabFocus();
            Activate();
            AcceptEvent();
        }
        else if (_carousel != null && e.IsActionPressed("ui_left", true))
        {
            _carousel.Step(-1);
            AcceptEvent();
        }
        else if (_carousel != null && e.IsActionPressed("ui_right", true))
        {
            _carousel.Step(1);
            AcceptEvent();
        }
        else if (e.IsActionPressed(Actions.Accept))
        {
            Activate();
            AcceptEvent();
        }
    }

    private void Activate()
    {
        if (_carousel != null) { _carousel.Step(1); }
        else { _pressed?.Invoke(); }
    }
}
