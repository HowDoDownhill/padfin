using Godot;

namespace Padfin;

/// <summary>A modal status line with a progress bar, for work the user waits on. B does not dismiss it.</summary>
public partial class ProgressOverlay : Overlay
{
    private readonly string _initial;
    private Label _status;
    private ProgressBar _bar;
    private float _value;

    public ProgressOverlay() { }
    public ProgressOverlay(string status) { _initial = status; }

    protected override void Build(PanelContainer panel)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        column.AddThemeConstantOverride("separation", 18);
        panel.AddChild(Ui.Pad(column, 32, 26, 32, 28));
        _status = Ui.Label(_initial ?? "", variation: "TitleLabel");
        column.AddChild(_status);
        _bar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14), Value = _value * 100 };
        column.AddChild(_bar);
    }

    /// <param name="fraction">Progress from 0 to 1.</param>
    public void SetProgress(float fraction)
    {
        _value = fraction;
        if (IsInstanceValid(_bar)) { _bar.Value = fraction * 100; }
    }

    public void SetStatus(string status)
    {
        if (IsInstanceValid(_status)) { _status.Text = status; }
    }

    /// <summary>Swallows B, so the work in progress is not left without its status.</summary>
    public override bool OnInput(InputEvent e) => e.IsActionPressed(Actions.Back);
}
