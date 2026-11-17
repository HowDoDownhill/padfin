using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>A modal text entry with the on-screen keyboard. A physical keyboard types into it too.</summary>
public partial class TextPrompt : Overlay
{
    private readonly TaskCompletionSource<string> _result = new();
    private readonly string _title;
    private readonly bool _secret;
    private string _text;
    private string _answer;
    private Label _display;
    private KeyboardGrid _keys;

    /// <summary>Completes with the entered text, or null when cancelled.</summary>
    public Task<string> Result => _result.Task;

    public TextPrompt() { }

    public TextPrompt(string title, string initial, bool secret)
    {
        _title = title;
        _secret = secret;
        _text = initial ?? "";
    }

    protected override void Build(PanelContainer panel)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 18);
        panel.AddChild(Ui.Pad(column, 32, 26, 32, 22));
        column.AddChild(Ui.Label(_title, 26, variation: "CaptionLabel"));

        var field = new PanelContainer();
        field.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Surface, 10, 2, Ui.Accent, 20, 14));
        _display = Ui.Label("", 34);
        _display.ClipText = true;
        _display.CustomMinimumSize = new Vector2(0, 46);
        field.AddChild(_display);
        column.AddChild(field);

        _keys = new KeyboardGrid(true);
        _keys.Typed += s => { _text += s; Refresh(); };
        _keys.Backspace += DeleteLast;
        _keys.Done += () => Finish(_text);
        column.AddChild(_keys);

        var hints = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        hints.AddThemeConstantOverride("separation", 26);
        hints.AddChild(Ui.Hint(Actions.X, "Delete", 22));
        hints.AddChild(Ui.Hint(Actions.Y, "Space", 22));
        hints.AddChild(Ui.Hint(Actions.Start, "Done", 22));
        hints.AddChild(Ui.Hint(Actions.Back, "Cancel", 22));
        column.AddChild(hints);
        Refresh();
    }

    private void DeleteLast()
    {
        if (_text.Length == 0) { return; }
        var cut = char.IsLowSurrogate(_text[^1]) && _text.Length > 1 ? 2 : 1;
        _text = _text[..^cut];
        Refresh();
    }

    private void Refresh() => _display.Text = (_secret ? new string('•', _text.Length) : _text) + "▏";

    private void Finish(string value)
    {
        if (_result.Task.IsCompleted) { return; }
        _answer = value;
        App.I.CloseOverlay(this);
    }

    public override void FocusDefault() => _keys.FocusFirst();

    public override bool OnRawInput(InputEvent e) => _keys.HandleKey(e);

    public override bool OnInput(InputEvent e)
    {
        if (e.IsActionPressed(Actions.X, true)) { DeleteLast(); return true; }
        if (e.IsActionPressed(Actions.Y)) { _text += " "; Refresh(); return true; }
        if (e.IsActionPressed(Actions.Start)) { Finish(_text); return true; }
        return false;
    }

    /// <summary>Answers once the prompt has closed and returned focus: the text, or null when cancelled.</summary>
    public override void OnDismissed() => _result.TrySetResult(_answer);
}
