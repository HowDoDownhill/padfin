using System;
using Godot;

namespace Padfin;

/// <summary>
/// Scrolls a ScrollContainer to follow focus, in place of Godot's follow_focus. Godot scrolls the
/// least it can, which leaves a row cut off at an edge and never returns it to where it started.
/// This puts the focused section at a fixed spot, or keeps the focused control clear of the edges
/// by a margin, and glides there.
/// </summary>
public partial class FocusScroller : Node
{
    private const double Duration = 0.2;

    private readonly ScrollContainer _scroll;
    private readonly Control _content;
    private readonly Control _sections;
    private readonly bool _vertical;
    private readonly float _margin;
    private float _target = -1;
    private Tween _tween;

    public FocusScroller() { }

    private FocusScroller(ScrollContainer scroll, Control content, bool vertical, float margin, Control sections)
    {
        _scroll = scroll;
        _content = content;
        _vertical = vertical;
        _margin = margin;
        _sections = sections;
        scroll.FollowFocus = false;
        scroll.AddChild(this, false, InternalMode.Back);
    }

    /// <summary>
    /// Aligns the section that holds the focused control to the start of the view. A section is a
    /// direct child of <paramref name="sections"/>, such as one row of a page.
    /// </summary>
    public static FocusScroller AlignSections(ScrollContainer scroll, Control content, Control sections, float margin, bool vertical = true) =>
        new(scroll, content, vertical, margin, sections);

    /// <summary>Keeps the focused control inside the view with a margin on both sides.</summary>
    public static FocusScroller KeepVisible(ScrollContainer scroll, Control content, float margin, bool vertical) =>
        new(scroll, content, vertical, margin, null);

    public override void _Ready() => GetViewport().GuiFocusChanged += OnFocusChanged;

    public override void _ExitTree() => GetViewport().GuiFocusChanged -= OnFocusChanged;

    private void OnFocusChanged(Control focused)
    {
        if (!IsInstanceValid(focused) || !_scroll.IsAncestorOf(focused)) { return; }
        // Layout settles at the end of the frame, which matters right after a screen builds its rows.
        Callable.From(() => Follow(focused)).CallDeferred();
    }

    private void Follow(Control focused)
    {
        if (!IsInstanceValid(focused) || !focused.IsInsideTree() || !IsInsideTree() || !IsInstanceValid(_content)) { return; }

        var view = Axis(_scroll.Size);
        var current = _target >= 0 ? _target : (_vertical ? _scroll.ScrollVertical : _scroll.ScrollHorizontal);
        float target;

        var section = _sections != null ? SectionOf(focused) : null;
        if (section != null) { target = Offset(section) - _margin; }
        else
        {
            var start = Offset(focused) - _margin;
            var end = Offset(focused) + Axis(focused.Size) + _margin - view;
            target = start < current ? start : end > current ? end : current;
        }

        target = Math.Clamp(target, 0, Math.Max(0, Axis(_content.Size) - view));
        if (Mathf.IsEqualApprox(target, current)) { return; }
        _target = target;
        _tween?.Kill();
        _tween = _scroll.CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_scroll, _vertical ? "scroll_vertical" : "scroll_horizontal", (int)target, Duration);
        _tween.TweenCallback(Callable.From(() => _target = -1));
    }

    private Control SectionOf(Control control)
    {
        Node node = control;
        while (node != null && node.GetParent() != _sections) { node = node.GetParent(); }
        return node as Control;
    }

    /// <summary>Distance of a control from the start of the scrolled content.</summary>
    private float Offset(Control control) => Axis(control.GlobalPosition - _content.GlobalPosition);

    private float Axis(Vector2 value) => _vertical ? value.Y : value.X;
}
