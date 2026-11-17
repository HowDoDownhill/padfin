using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>A poster grid for a library, collection or folder. Loads a page at a time as focus nears the end.</summary>
public partial class LibraryScreen : Screen
{
    private const int PageSize = 100;
    private const int CardWidth = 220;

    private static readonly (string Label, string SortBy, bool Descending)[] Sorts =
    {
        ("Name", "SortName", false),
        ("Date Added", "DateCreated,SortName", true),
        ("Release Date", "PremiereDate,SortName", true),
        ("Rating", "CommunityRating,SortName", true),
    };

    private readonly string _title;
    private readonly string _parentId;
    private readonly string _types;
    private readonly bool _recursive;
    private readonly string _tab;
    private List<BaseItem> _genres;
    private string _genreId;
    private string _genreName;
    private readonly List<Card> _cards = new();
    private ScrollContainer _scroll;
    private HFlowContainer _grid;
    private int _sort = Math.Clamp(Config.I.LibrarySort, 0, Sorts.Length - 1);

    /// <summary>The names of the sort orders, for the default-sort setting.</summary>
    public static IEnumerable<string> SortNames => Sorts.Select(s => s.Label);
    private int _total = -1;
    private int _generation;
    private bool _loading;

    public override IEnumerable<(string, string)> Hints =>
        new[] { (Actions.Accept, "Select"), (Actions.X, "Sort"), (Actions.Y, "Search"), (Actions.Rb, "Hold: Genres"), (Actions.Back, "Back") };

    public override string Tab => _tab;
    public override string Title => _title;

    public LibraryScreen() { }

    /// <summary>The contents of a library, collection or folder.</summary>
    public LibraryScreen(BaseItem parent)
    {
        _title = parent.Name;
        _parentId = parent.Id;
        (_types, _recursive) = parent.CollectionType switch
        {
            "movies" => ("Movie", true),
            "tvshows" => ("Series", true),
            "boxsets" => ("BoxSet", true),
            "homevideos" => ("Video,Movie", true),
            _ => (null, false),
        };
    }

    /// <summary>A sidebar page: every item of one type across all libraries.</summary>
    public LibraryScreen(string tab, string title, string types)
    {
        _tab = tab;
        _title = title;
        _types = types;
        _recursive = true;
    }

    public override void OnEnter()
    {
        var column = new VBoxContainer();
        Ui.Fill(column);
        AddChild(column);

        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(_scroll);
        _grid = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 30);
        _grid.AddThemeConstantOverride("v_separation", 26);
        var content = Ui.Pad(_grid, Ui.Margin, Card.ShadowAbove, Ui.Margin, 60);
        _scroll.AddChild(content);
        FocusScroller.KeepVisible(_scroll, content, 60, vertical: true);
        _scroll.GetVScrollBar().ValueChanged += _ => UpdateImages();

        _ = LoadPageAsync();
    }

    public override void FocusDefault()
    {
        if (_cards.Count > 0) { _cards[0].GrabFocus(); }
    }

    private async Task LoadPageAsync()
    {
        if (_loading || (_total >= 0 && _cards.Count >= _total)) { return; }
        _loading = true;
        var generation = _generation;
        try
        {
            var sort = Sorts[_sort];
            var result = await Jellyfin.I.ItemsAsync(_parentId, _types, _recursive, sort.SortBy, sort.Descending, _cards.Count, PageSize, _genreId);
            if (!IsInstanceValid(this) || generation != _generation) { return; }
            _total = result.TotalRecordCount;
            var wasEmpty = _cards.Count == 0;
            foreach (var item in result.Items)
            {
                var card = new Card(item, CardShape.Poster, CardWidth, deferImage: true);
                var index = _cards.Count;
                card.Pressed += () => App.I.Open(card.Item);
                card.FocusEntered += () => OnCardFocused(index);
                _grid.AddChild(card);
                _cards.Add(card);
            }
            if (result.Items.Count == 0) { _total = _cards.Count; }
            SetInfo($"{_total} items  ·  {(_genreName == null ? "" : _genreName + "  ·  ")}Sorted by {sort.Label}");
            if (wasEmpty && App.I.Current == this) { FocusDefault(); }
            if (_total == 0) { SetInfo("Nothing here yet"); }
            // Layout settles on the next frame, which is when card positions are known.
            Callable.From(UpdateImages).CallDeferred();
        }
        catch (JellyfinException e) { App.I.Toast(e.Message); }
        finally { _loading = false; }
    }

    private void OnCardFocused(int index)
    {
        if (index >= _cards.Count - 30) { _ = LoadPageAsync(); }
    }

    /// <summary>Loads artwork for cards near the visible area and frees it for cards far away.</summary>
    private void UpdateImages()
    {
        if (!IsInstanceValid(_scroll)) { return; }
        var view = _scroll.GetGlobalRect();
        var near = view.Grow(view.Size.Y * 1.5f);
        var far = view.Grow(view.Size.Y * 4f);
        foreach (var card in _cards)
        {
            var rect = card.GetGlobalRect();
            if (near.Intersects(rect)) { card.LoadImage(); }
            else if (!far.Intersects(rect)) { card.UnloadImage(); }
        }
    }

    private void CycleSort()
    {
        _sort = (_sort + 1) % Sorts.Length;
        Reload();
    }

    /// <summary>Opens the genre jump grid. Choosing a genre filters this page to it.</summary>
    public override bool OnShoulderHold()
    {
        _ = ShowGenresAsync();
        return true;
    }

    private async Task ShowGenresAsync()
    {
        try { _genres ??= await Jellyfin.I.GenresAsync(_parentId, _types); }
        catch (JellyfinException e)
        {
            App.I.Toast(e.Message);
            return;
        }
        if (!IsInstanceValid(this) || App.I.Current != this) { return; }
        if (_genres.Count == 0)
        {
            App.I.Toast("No genres here.");
            return;
        }
        var tiles = new List<(string, string)> { ("All", null) };
        tiles.AddRange(_genres.Select(g => (g.Name, (string)null)));
        var selected = _genreId == null ? 0 : _genres.FindIndex(g => g.Id == _genreId) + 1;
        App.I.ShowJump("Genres", tiles, selected, index =>
        {
            var genre = index == 0 ? null : _genres[index - 1];
            if (genre?.Id == _genreId) { return; }
            _genreId = genre?.Id;
            _genreName = genre?.Name;
            Reload();
        });
    }

    /// <summary>Empties the grid and loads it again with the current sort and genre.</summary>
    private void Reload()
    {
        _generation++;
        _loading = false;
        _total = -1;
        foreach (var card in _cards) { card.QueueFree(); }
        _cards.Clear();
        _scroll.ScrollVertical = 0;
        _ = LoadPageAsync();
    }

    public override bool OnInput(InputEvent e)
    {
        if (e.IsActionPressed(Actions.X))
        {
            CycleSort();
            return true;
        }
        if (e.IsActionPressed(Actions.Y))
        {
            App.I.Push(new SearchScreen());
            return true;
        }
        return false;
    }
}
