using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>
/// Search with the on-screen keyboard on the left and live results on the right, in two sections:
/// what is on the server, and what Seerr can get.
/// </summary>
public partial class SearchScreen : Screen
{
    private const string Prompt = "Type to search movies, shows and episodes.";

    private string _query = "";
    private Label _display;
    private Label _status;
    private KeyboardGrid _keys;
    private VBoxContainer _sections;
    private int _generation;

    public override string Title => "Search";

    public override IEnumerable<(string, string)> Hints =>
        new[] { (Actions.Accept, "Select"), (Actions.X, "Delete"), (Actions.Y, "Space"), (Actions.Back, "Back") };

    public override void OnEnter()
    {
        var columns = new HBoxContainer();
        Ui.Fill(columns);
        columns.AddThemeConstantOverride("separation", 30);
        AddChild(columns);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 18);
        left.AddChild(Ui.Label("Search", variation: "DisplayLabel"));
        var field = new PanelContainer();
        field.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Surface, 10, 2, Ui.Accent, 20, 14));
        _display = Ui.Label("▏", 32);
        _display.ClipText = true;
        _display.CustomMinimumSize = new Vector2(0, 44);
        field.AddChild(_display);
        left.AddChild(field);
        _keys = new KeyboardGrid(false, "Clear");
        _keys.Typed += s => SetQuery(_query + s);
        _keys.Backspace += DeleteLast;
        _keys.Done += () => SetQuery("");
        left.AddChild(_keys);
        var keyboard = Ui.MicaPanel(left, 28, 22);
        keyboard.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var leftPad = Ui.Pad(keyboard, Ui.PanelGap, Ui.PanelGap, 0, 0);
        leftPad.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        columns.AddChild(leftPad);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _status = Ui.Label(Prompt, 26, variation: "CaptionLabel");
        right.AddChild(Ui.Pad(_status, 0, 30, 0, 6));
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _sections = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sections.AddThemeConstantOverride("separation", 10);
        var content = Ui.Pad(_sections, 12, 6, Ui.Margin, 60);
        scroll.AddChild(content);
        // The margin is tall enough to keep part of the neighbouring row in view across a section
        // heading. A fully hidden row could not be reached with Up or Down.
        FocusScroller.KeepVisible(scroll, content, 140, vertical: true);
        right.AddChild(scroll);
        columns.AddChild(right);

        _ = Seerr.I.RestoreAsync();
    }

    public override void FocusDefault() => _keys.FocusFirst();

    private void DeleteLast()
    {
        if (_query.Length > 0) { SetQuery(_query[..^1]); }
    }

    private void SetQuery(string query)
    {
        _query = query;
        _display.Text = _query + "▏";
        _ = SearchAsync();
    }

    private async Task SearchAsync()
    {
        var generation = ++_generation;
        await Task.Delay(350); // Wait for a pause in typing.
        if (generation != _generation || !IsInstanceValid(this)) { return; }

        var term = _query.Trim();
        var onServer = new List<BaseItem>();
        var onSeerr = new List<SeerrItem>();
        if (term.Length > 0)
        {
            _status.Text = "Searching…";
            // The two searches run side by side, and one failing leaves the other's results standing.
            var server = ServerAsync(term);
            var seerr = SeerrAsync(term);
            onServer = await server;
            onSeerr = await seerr;
            if (generation != _generation || !IsInstanceValid(this)) { return; }
        }

        foreach (var child in _sections.GetChildren())
        {
            _sections.RemoveChild(child);
            child.QueueFree();
        }
        AddSection("On Server", onServer.Select(item =>
        {
            var card = new Card(item, CardShape.Poster, 200);
            card.Pressed += () => App.I.Open(card.Item);
            return card;
        }));
        AddSection("On Seerr", onSeerr.Select(item =>
        {
            var card = new Card(item, 200);
            card.Pressed += () => App.I.Push(new SeerrDetailsScreen(card.Discovered));
            return card;
        }));

        _status.Text = term.Length == 0 ? Prompt
            : onServer.Count + onSeerr.Count == 0 ? $"Nothing matches \"{term}\"."
            : Seerr.I.Connected ? $"{onServer.Count} on the server  ·  {onSeerr.Count} on Seerr"
            : $"{onServer.Count} on the server  ·  Connect Seerr on the Requests page to search it too";
    }

    private static async Task<List<BaseItem>> ServerAsync(string term)
    {
        try { return (await Jellyfin.I.SearchAsync(term)).Items; }
        catch (JellyfinException e)
        {
            App.I.Toast(e.Message);
            return new List<BaseItem>();
        }
    }

    /// <summary>Titles Seerr knows that the server does not already have in full.</summary>
    private static async Task<List<SeerrItem>> SeerrAsync(string term)
    {
        await Seerr.I.RestoreAsync();
        if (!Seerr.I.Connected) { return new List<SeerrItem>(); }
        try
        {
            return (await Seerr.I.SearchAsync(term)).Where(item => item.MediaInfo?.Status != SeerrMedia.Available).ToList();
        }
        catch (SeerrException e)
        {
            App.I.Toast(e.Message);
            return new List<SeerrItem>();
        }
    }

    private void AddSection(string title, IEnumerable<Card> cards)
    {
        var list = cards.ToList();
        if (list.Count == 0) { return; }
        _sections.AddChild(Ui.Pad(Ui.Label(title, variation: "TitleLabel"), 12, 14, 0, 0));
        var grid = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 24);
        foreach (var card in list) { grid.AddChild(card); }
        _sections.AddChild(grid);
    }

    public override bool OnRawInput(InputEvent e) => _keys.HandleKey(e);

    public override bool OnInput(InputEvent e)
    {
        if (e.IsActionPressed(Actions.X, true))
        {
            DeleteLast();
            return true;
        }
        if (e.IsActionPressed(Actions.Y))
        {
            SetQuery(_query + " ");
            return true;
        }
        return false;
    }
}
