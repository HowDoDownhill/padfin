using System;
using System.Collections.Generic;
using Godot;

namespace Padfin;

/// <summary>
/// A page of card rows that scrolls up and down. The focused row always moves to where the first
/// row sits, title included, and Up and Down are linked row to row.
/// </summary>
public partial class RowPage : ScrollContainer
{
    private const int RowTop = 14;

    private readonly VBoxContainer _rows;

    public List<CardRow> Rows { get; } = new();

    public RowPage()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        VerticalScrollMode = ScrollMode.ShowNever;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 0); // Each row already pads for its cards' shadows.
        var content = Ui.Pad(_rows, 0, RowTop, 0, 0);
        AddChild(content);
        FocusScroller.AlignSections(this, content, _rows, RowTop);
    }

    public void Clear()
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        Rows.Clear();
    }

    /// <summary>Shows a line of text in place of rows, for loading and empty states.</summary>
    public Label ShowMessage(string text)
    {
        var label = Ui.Label(text, 28, variation: "CaptionLabel");
        _rows.AddChild(Ui.Pad(label, Ui.Margin, 40, Ui.Margin, 0));
        return label;
    }

    /// <summary>Adds any control as a block above or between the rows.</summary>
    public void AddBlock(Control block) => _rows.AddChild(block);

    public void Add(CardRow row)
    {
        if (row.Cards.Count == 0)
        {
            row.QueueFree();
            return;
        }
        _rows.AddChild(row);
        Rows.Add(row);
        var index = Rows.Count - 1;
        foreach (var card in row.Cards) { card.FocusEntered += () => Link(card, index); }
    }

    /// <summary>Call after the last row: leaves room below it so it can reach the top position too.</summary>
    public void Finish() => _rows.AddChild(Ui.Spacer(0, 700));

    /// <summary>
    /// Points Up and Down from a card at the rows above and below. A row scrolled out of view is
    /// clipped, and Godot's own search for a focus neighbour skips clipped controls.
    /// </summary>
    private void Link(Card card, int index)
    {
        card.FocusNeighborTop = index > 0 ? Rows[index - 1].Entry.GetPath() : card.GetPath();
        card.FocusNeighborBottom = index < Rows.Count - 1 ? Rows[index + 1].Entry.GetPath() : card.GetPath();
    }

    /// <summary>Row and card index of the focused card, or the first card when focus is elsewhere.</summary>
    public (int Row, int Card) FocusedPosition()
    {
        var owner = GetViewport()?.GuiGetFocusOwner();
        for (var r = 0; r < Rows.Count; r++)
        {
            var c = Rows[r].Cards.FindIndex(card => card == owner);
            if (c >= 0) { return (r, c); }
        }
        return (0, 0);
    }

    public void Focus(int row = 0, int card = 0)
    {
        if (Rows.Count == 0) { return; }
        var target = Rows[Math.Clamp(row, 0, Rows.Count - 1)];
        target.Cards[Math.Clamp(card, 0, target.Cards.Count - 1)].GrabFocus();
    }
}
