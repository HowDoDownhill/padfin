using Godot;

namespace Padfin;

/// <summary>
/// A modal layer above the screens: menus and the text prompt. It is a RomBox UiPanel, so it gets
/// the frosted panel, drop shadow and scale-fade transition.
/// </summary>
public abstract partial class Overlay : UiPanel
{
    public Control PreviousFocus;

    protected Overlay()
    {
        BackdropDim = 0.45f;
        CenterContent = true;
    }

    public override void _Ready()
    {
        base._Ready();
        Build((PanelContainer)ContentRoot);
    }

    /// <summary>Fills the frosted panel with the overlay's content.</summary>
    protected abstract void Build(PanelContainer panel);

    public virtual bool OnInput(InputEvent e) => false;
    public virtual bool OnRawInput(InputEvent e) => false;
    public virtual void FocusDefault() { }

    /// <summary>Called when the overlay is dismissed, before it fades out.</summary>
    public virtual void OnDismissed() { }
}
