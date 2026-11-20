using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>
/// A poster grid for a library, collection or folder. Loads a page at a time as focus nears the
/// end. LT and RT step through the alphabet while sorted by name, and by a screenful otherwise.
/// </summary>
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
    private CardGrid _grid;
    private Task _load;
    private int _sort = Math.Clamp(Config.I.LibrarySort, 0, Sorts.Length - 1);

    /// <summary>The names of the sort orders, for the default-sort setting.</summary>
    public static IEnumerable<string> SortNames => Sorts.Select(s => s.Label);
    private int _total = -1;
    private int _generation;

    public override IEnumerable<(string, string)> Hints =>
        new[] { (Actions.Accept, "Select"), (Actions.X, "Sort"), (Actions.Y, "Search"), (Actions.Rb, "Hold: Genres"), (Actions.Back, "Back") };

    public override string Tab => _tab;
    public override string Title => _title;
    public override string TriggerLabel => SortedByName ? "Letter" : "Page";

    private bool SortedByName => Sorts[_sort].SortBy == "SortName";

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
        _grid = new CardGrid
        {
            LoadsImagesNearView = true,
            // Jellyfin files a title under its sort name, so "The Matrix" sits with the Ms.
            LetterOf = card => CardGrid.Initial(((Card)card).Item?.SortName ?? card.Title),
        };
        _grid.NearEnd += () => _ = LoadPageAsync();
        Ui.Fill(_grid);
        AddChild(_grid);

        _ = LoadPageAsync();
    }

    public override void FocusDefault() => _grid.Focus();

    private bool AllLoaded => _total >= 0 && _grid.Cards.Count >= _total;

    /// <summary>Loads the next page. A call made while a page is on its way waits for that page.</summary>
    private Task LoadPageAsync()
    {
        if (_load is { IsCompleted: false }) { return _load; }
        return _load = AllLoaded ? Task.CompletedTask : LoadNextPageAsync();
    }

    private async Task LoadNextPageAsync()
    {
        var generation = _generation;
        try
        {
            var sort = Sorts[_sort];
            var result = await Jellyfin.I.ItemsAsync(_parentId, _types, _recursive, sort.SortBy, sort.Descending, _grid.Cards.Count, PageSize, _genreId);
            if (!IsInstanceValid(this) || generation != _generation) { return; }
            _total = result.TotalRecordCount;
            var wasEmpty = _grid.Cards.Count == 0;
            foreach (var item in result.Items)
            {
                var card = new Card(item, CardShape.Poster, CardWidth, deferImage: true);
                _grid.Add(card, _ => App.I.Open(card.Item));
            }
            if (result.Items.Count == 0) { _total = _grid.Cards.Count; }
            SetInfo($"{_total} items  ·  {(_genreName == null ? "" : _genreName + "  ·  ")}Sorted by {sort.Label}");
            if (wasEmpty && App.I.Current == this) { FocusDefault(); }
            if (_total == 0) { SetInfo("Nothing here yet"); }
        }
        catch (JellyfinException e)
        {
            // Stops a caller that is waiting for more cards from asking again.
            _total = _grid.Cards.Count;
            App.I.Toast(e.Message);
        }
    }

    /// <summary>LT and RT: the previous or next letter while sorted by name, a screenful otherwise.</summary>
    public override bool OnTrigger(int direction)
    {
        if (SortedByName) { _ = StepLetterAsync(direction); }
        else { _grid.StepPage(direction); }
        return true;
    }

    /// <summary>Steps to the next letter, loading pages until a card of that letter has arrived.</summary>
    private async Task StepLetterAsync(int direction)
    {
        var generation = _generation;
        while (!_grid.StepLetter(direction))
        {
            if (direction < 0 || AllLoaded) { return; }
            await LoadPageAsync();
            if (!IsInstanceValid(this) || generation != _generation || App.I.Current != this) { return; }
        }
    }

    private void CycleSort()
    {
        _sort = (_sort + 1) % Sorts.Length;
        Reload();
        App.I.RefreshChrome(); // The trigger prompt names what the triggers do under this sort.
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
        _load = null;
        _total = -1;
        _grid.Clear();
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
