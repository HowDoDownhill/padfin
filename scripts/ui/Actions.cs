using Godot;

namespace Padfin;

/// <summary>
/// Input actions. Godot's built-in ui_accept and ui_cancel hold only keyboard keys, so A and B are
/// added to them here. The D-pad and left stick already drive ui_left/right/up/down.
/// </summary>
public static class Actions
{
    public const string Accept = "ui_accept";
    public const string Back = "ui_cancel";
    public const string X = "pf_x";
    public const string Y = "pf_y";
    public const string Lb = "pf_lb";
    public const string Rb = "pf_rb";
    public const string Start = "pf_start";
    public const string Fullscreen = "pf_fullscreen";

    public static void Register()
    {
        if (InputMap.HasAction(X)) { return; }
        InputMap.ActionAddEvent(Accept, new InputEventJoypadButton { ButtonIndex = JoyButton.A, Device = -1 });
        InputMap.ActionAddEvent(Back, new InputEventJoypadButton { ButtonIndex = JoyButton.B, Device = -1 });
        Add(X, JoyButton.X, Key.F2);
        Add(Y, JoyButton.Y, Key.F3);
        Add(Lb, JoyButton.LeftShoulder, Key.Pageup);
        Add(Rb, JoyButton.RightShoulder, Key.Pagedown);
        Add(Start, JoyButton.Start, Key.F1);
        Add(Fullscreen, null, Key.F11);
    }

    private static void Add(string name, JoyButton? button, Key key)
    {
        InputMap.AddAction(name);
        if (button != null) { InputMap.ActionAddEvent(name, new InputEventJoypadButton { ButtonIndex = button.Value, Device = -1 }); }
        InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = key });
    }
}
