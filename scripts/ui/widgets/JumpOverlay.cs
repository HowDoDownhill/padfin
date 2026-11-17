using System;
using System.Collections.Generic;
using Godot;

namespace Padfin;

/// <summary>
/// A grid of tiles for jumping straight to one of many choices, such as a genre. It is Padfin's
/// version of RomBox's system jump popup: list-button tiles in a frosted panel, D-pad moves by
/// row and column, and focus stays inside the grid.
/// </summary>
public partial class JumpOverlay : Overlay
{
    private const int Columns = 5;
    private const int TileWidth = 220;
    private const int VisibleRows = 5;

    private readonly string _title;
    private readonly IReadOnlyList<(string Label, string Caption)> _tiles;
    private readonly Action<int> _chosen;
    private readonly int _initial;
    private readonly List<Button> _buttons = new();

    public JumpOverlay() { }

    /// <param name="tiles">Each tile's label and an optional smaller caption under it.</param>
    public JumpOverlay(string title, IReadOnlyList<(string Label, string Caption)> tiles, int selected, Action<int> chosen)
    {
        _title = title;
        _tiles = tiles;
        _chosen = chosen;
        _initial = Math.Clamp(selected, 0, Math.Max(0, tiles.Count - 1));
        BackdropDim = 0.7f;
    }

    protected override void Build(PanelContainer panel)
    {
        var hasCaptions = false;
        foreach (var tile in _tiles) { hasCaptions |= !string.IsNullOrEmpty(tile.Caption); }
        var tileHeight = hasCaptions ? 96 : 72;

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(Ui.Pad(column, 30, 24, 30, 30));
        column.AddChild(Ui.Label(_title, variation: "TitleLabel"));

        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 12);

        var rows = (_tiles.Count + Columns - 1) / Columns;
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = rows > VisibleRows ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.ShowNever,
            CustomMinimumSize = new Vector2(Columns * TileWidth + (Columns - 1) * 14 + 24, Math.Min(rows, VisibleRows) * (tileHeight + 12) + 12),
        };
        var content = Ui.Pad(grid, 6, 6, 6, 6);
        scroll.AddChild(content);
        column.AddChild(scroll);
        FocusScroller.KeepVisible(scroll, content, 12, vertical: true);

        for (var i = 0; i < _tiles.Count; i++)
        {
            var index = i;
            var button = Ui.Button("");
            button.CustomMinimumSize = new Vector2(TileWidth, tileHeight);
            button.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Surface, 10));
            button.AddThemeStyleboxOverride("hover", Ui.Box(Ui.SurfaceHi, 10));

            var text = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            text.AddThemeConstantOverride("separation", 0);
            text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 10);
            var label = Ui.Label(_tiles[i].Label, 21);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.ClipText = true;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            text.AddChild(label);
            if (!string.IsNullOrEmpty(_tiles[i].Caption))
            {
                var caption = Ui.Label(_tiles[i].Caption, 18, variation: "CaptionLabel");
                caption.HorizontalAlignment = HorizontalAlignment.Center;
                text.AddChild(caption);
            }
            button.AddChild(text);

            button.Pressed += () =>
            {
                App.I.CloseOverlay(this);
                _chosen?.Invoke(index);
            };
            grid.AddChild(button);
            _buttons.Add(button);
        }

        // Moves go by row and column and stop at the edges, so focus cannot leave the grid.
        for (var i = 0; i < _buttons.Count; i++)
        {
            var button = _buttons[i];
            var col = i % Columns;
            button.FocusNeighborLeft = (col > 0 ? _buttons[i - 1] : button).GetPath();
            button.FocusNeighborRight = (col < Columns - 1 && i + 1 < _buttons.Count ? _buttons[i + 1] : button).GetPath();
            button.FocusNeighborTop = (i - Columns >= 0 ? _buttons[i - Columns] : button).GetPath();
            button.FocusNeighborBottom = (i + Columns < _buttons.Count ? _buttons[i + Columns] : button).GetPath();
            button.FocusNext = button.FocusNeighborRight;
            button.FocusPrevious = button.FocusNeighborLeft;
        }
    }

    public override void FocusDefault()
    {
        if (_buttons.Count > 0) { _buttons[_initial].GrabFocus(); }
    }
}
