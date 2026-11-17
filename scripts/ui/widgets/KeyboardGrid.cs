using System;
using System.Collections.Generic;
using Godot;

namespace Padfin;

/// <summary>An on-screen keyboard laid out for D-pad use. It reports keys; the owner keeps the text.</summary>
public partial class KeyboardGrid : VBoxContainer
{
    private static readonly string[] Lower = { "1234567890", "qwertyuiop", "asdfghjkl-", "zxcvbnm.:/" };
    private static readonly string[] Upper = { "!@#$%&*()+", "QWERTYUIOP", "ASDFGHJKL_", "ZXCVBNM,;?" };

    public event Action<string> Typed;
    public event Action Backspace;
    public event Action Done;

    private readonly List<List<Button>> _rows = new();
    private readonly bool _confine;
    private bool _shift;
    private Button _shiftKey;

    public KeyboardGrid() { }

    /// <param name="confine">Stops focus leaving the keyboard, for modal use.</param>
    public KeyboardGrid(bool confine, string doneLabel = "Done")
    {
        _confine = confine;
        AddThemeConstantOverride("separation", 8);
        for (var r = 0; r < Lower.Length; r++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var keys = new List<Button>();
            for (var c = 0; c < Lower[r].Length; c++)
            {
                var (rr, cc) = (r, c);
                var key = MakeKey(Lower[r][c].ToString(), 72);
                key.Pressed += () => Typed?.Invoke((_shift ? Upper : Lower)[rr][cc].ToString());
                row.AddChild(key);
                keys.Add(key);
            }
            AddChild(row);
            _rows.Add(keys);
        }

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        _shiftKey = MakeKey("Shift", 152);
        _shiftKey.Pressed += ToggleShift;
        var space = MakeKey("Space", 312);
        space.Pressed += () => Typed?.Invoke(" ");
        var back = MakeKey("Delete", 152);
        back.Pressed += () => Backspace?.Invoke();
        var done = MakeKey(doneLabel, 152);
        done.Pressed += () => Done?.Invoke();
        foreach (var key in new[] { _shiftKey, space, back, done }) { bottom.AddChild(key); }
        AddChild(bottom);
        _rows.Add(new List<Button> { _shiftKey, space, back, done });
    }

    private static Button MakeKey(string text, int width)
    {
        var key = Ui.Button(text);
        key.CustomMinimumSize = new Vector2(width, 72);
        key.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Surface, 10));
        key.AddThemeStyleboxOverride("hover", Ui.Box(Ui.SurfaceHi, 10));
        return key;
    }

    public override void _Ready()
    {
        for (var r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var key = row[c];
                if (c == 0) { key.FocusNeighborLeft = row[^1].GetPath(); }
                if (c == row.Count - 1 && _confine) { key.FocusNeighborRight = row[0].GetPath(); }
                if (r == 0) { key.FocusNeighborTop = key.GetPath(); }
                if (r == _rows.Count - 1) { key.FocusNeighborBottom = key.GetPath(); }
            }
        }
    }

    public void ToggleShift()
    {
        _shift = !_shift;
        for (var r = 0; r < Lower.Length; r++)
        {
            for (var c = 0; c < Lower[r].Length; c++) { _rows[r][c].Text = (_shift ? Upper : Lower)[r][c].ToString(); }
        }
        _shiftKey.Text = _shift ? "SHIFT" : "Shift";
    }

    public void FocusFirst() => _rows[1][0].GrabFocus();

    /// <summary>Feeds a physical keyboard event. True when it was a typing key.</summary>
    public bool HandleKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) { return false; }
        if (key.Keycode == Key.Backspace)
        {
            Backspace?.Invoke();
            return true;
        }
        if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            Done?.Invoke();
            return true;
        }
        if (key.Unicode >= 32 && !key.CtrlPressed && !key.AltPressed)
        {
            Typed?.Invoke(char.ConvertFromUtf32((int)key.Unicode));
            return true;
        }
        return false;
    }
}
