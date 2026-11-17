using System;
using System.Collections.Generic;
using Godot;

namespace Padfin;

/// <summary>A modal list of choices, used for the Start menu and track pickers.</summary>
public partial class MenuOverlay : Overlay
{
    private readonly List<Control> _buttons = new();
    private readonly string _title;
    private readonly IReadOnlyList<(string Label, Action Action)> _items;
    private readonly int _initial;
    private readonly Action _dismissed;
    private readonly string _body;
    private bool _chosen;

    public MenuOverlay() { }

    /// <param name="dismissed">Called when the menu closes without a choice.</param>
    /// <param name="body">Text shown between the title and the choices, for a question that needs explaining.</param>
    public MenuOverlay(string title, IReadOnlyList<(string Label, Action Action)> items, int selected = 0, Action dismissed = null, string body = null)
    {
        _dismissed = dismissed;
        _body = body;
        _title = title;
        _items = items;
        _initial = Math.Clamp(selected, 0, Math.Max(0, items.Count - 1));
    }

    protected override void Build(PanelContainer panel)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(Ui.Pad(column, 14, 16, 14, 16));
        column.AddChild(Ui.Pad(Ui.Label(_title, variation: "TitleLabel"), 18, 0, 18, 6));
        if (!string.IsNullOrEmpty(_body))
        {
            var body = Ui.Label(_body, 24);
            body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            body.CustomMinimumSize = new Vector2(680, 0);
            column.AddChild(Ui.Pad(body, 18, 0, 18, 14));
        }

        var scroll = new ScrollContainer
        {
            FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = _items.Count > 10 ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.ShowNever,
            CustomMinimumSize = new Vector2(0, Math.Min(_items.Count, 10) * 52),
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(list);
        column.AddChild(scroll);

        foreach (var (label, action) in _items)
        {
            var button = Ui.Button(label);
            button.Alignment = HorizontalAlignment.Left;
            button.ClipText = true;
            button.Pressed += () =>
            {
                _chosen = true;
                App.I.CloseOverlay(this);
                action?.Invoke();
            };
            list.AddChild(button);
            _buttons.Add(button);
        }
        Ui.ConfineVertical(_buttons);
    }

    public override void OnDismissed()
    {
        if (!_chosen) { _dismissed?.Invoke(); }
    }

    public override void FocusDefault()
    {
        if (_buttons.Count > 0) { _buttons[_initial].GrabFocus(); }
    }
}
