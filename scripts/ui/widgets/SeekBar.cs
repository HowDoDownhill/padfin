using System.Collections.Generic;
using Godot;

namespace Padfin;

/// <summary>The playback position bar in the player overlay.</summary>
public partial class SeekBar : Control
{
    private float _value;

    /// <summary>Position from 0 to 1.</summary>
    public float Value
    {
        get => _value;
        set
        {
            value = Mathf.Clamp(value, 0, 1);
            if (Mathf.IsEqualApprox(value, _value)) { return; }
            _value = value;
            QueueRedraw();
        }
    }

    private float? _preview;
    private IReadOnlyList<float> _marks = System.Array.Empty<float>();

    /// <summary>Where a seek in progress would land, from 0 to 1. Null when not scrubbing.</summary>
    public float? Preview
    {
        get => _preview;
        set
        {
            if (_preview == value) { return; }
            _preview = value;
            QueueRedraw();
        }
    }

    /// <summary>Positions from 0 to 1 to mark on the bar, such as chapter starts.</summary>
    public IReadOnlyList<float> Marks
    {
        set
        {
            _marks = value ?? System.Array.Empty<float>();
            QueueRedraw();
        }
    }

    public SeekBar()
    {
        CustomMinimumSize = new Vector2(0, 24);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var y = Size.Y / 2 - 4;
        DrawRect(new Rect2(0, y, Size.X, 8), new Color(1, 1, 1, 0.25f));
        DrawRect(new Rect2(0, y, Size.X * _value, 8), Ui.Accent);
        foreach (var mark in _marks)
        {
            if (mark is > 0.005f and < 0.995f) { DrawRect(new Rect2(Size.X * mark - 1, y - 3, 2, 14), new Color(1, 1, 1, 0.7f)); }
        }
        if (_preview is { } preview)
        {
            // While scrubbing the knob shows where the seek will land, and playback keeps its own fill.
            DrawRect(new Rect2(Size.X * Mathf.Min(_value, preview), y, Size.X * Mathf.Abs(preview - _value), 8), new Color(1, 1, 1, 0.45f));
            DrawCircle(new Vector2(Size.X * preview, Size.Y / 2), 13, Colors.White);
        }
        else { DrawCircle(new Vector2(Size.X * _value, Size.Y / 2), 11, Colors.White); }
    }
}
