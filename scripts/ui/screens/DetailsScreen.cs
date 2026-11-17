using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>Details for a movie, episode or series. A series also lists its seasons and episodes.</summary>
public partial class DetailsScreen : Screen
{
    private BaseItem _item;
    private readonly string _initialSeasonId;
    private TextureRect _backdrop;
    private TextureRect _poster;
    private VBoxContainer _info;
    private HBoxContainer _buttons;
    private VBoxContainer _below;
    private HBoxContainer _seasonBar;
    private Control _episodeHolder;
    private List<BaseItem> _seasons = new();
    private string _seasonId;
    private BaseItem _playTarget;
    private bool _built;
    private string _versionId;

    public override IEnumerable<(string, string)> Hints => new[] { (Actions.Accept, "Select"), (Actions.Back, "Back") };

    public DetailsScreen() { }

    public DetailsScreen(BaseItem item)
    {
        // A season opens its series with that season selected.
        if (item.Type == "Season" && item.SeriesId != null)
        {
            _initialSeasonId = item.Id;
            _item = new BaseItem { Id = item.SeriesId, Name = item.SeriesName, Type = "Series" };
        }
        else { _item = item; }
    }

    public override void OnEnter()
    {
        _backdrop = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            Modulate = new Color(1, 1, 1, 0.28f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Ui.Fill(_backdrop);
        AddChild(_backdrop);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
        };
        Ui.Fill(scroll);
        AddChild(scroll);

        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 30);
        scroll.AddChild(page);
        // The title block and the seasons block each snap to the top when focus enters them.
        FocusScroller.AlignSections(scroll, page, page, 20);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 50);
        _poster = new TextureRect
        {
            CustomMinimumSize = new Vector2(320, 480),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        top.AddChild(_poster);
        _info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _info.AddThemeConstantOverride("separation", 14);
        top.AddChild(_info);
        page.AddChild(Ui.Pad(top, Ui.Margin, 70, Ui.Margin, 0));

        _below = new VBoxContainer();
        _below.AddThemeConstantOverride("separation", 16);
        page.AddChild(_below);
        page.AddChild(Ui.Spacer(0, 40));

        _info.AddChild(Ui.Label(_item.Name, 60));
        _ = LoadAsync(true);
    }

    public override void OnResume() => _ = LoadAsync(false);

    public override void FocusDefault()
    {
        if (_buttons != null && _buttons.GetChildCount() > 0) { ((Control)_buttons.GetChild(0)).GrabFocus(); }
    }

    private async Task LoadAsync(bool first)
    {
        try
        {
            var item = await Jellyfin.I.ItemAsync(_item.Id);
            if (!IsInstanceValid(this) || item == null) { return; }
            _item = item;

            if (_item.Type == "Series")
            {
                _seasons = await Jellyfin.I.SeasonsAsync(_item.Id);
                var nextUp = await Jellyfin.I.NextUpAsync(_item.Id, 1);
                _playTarget = nextUp.FirstOrDefault();
                if (!IsInstanceValid(this)) { return; }
                _seasonId ??= _initialSeasonId ?? _playTarget?.SeasonId ?? _seasons.FirstOrDefault()?.Id;
            }
            else { _playTarget = _item; }

            var focusIndex = FocusedButtonIndex();
            BuildInfo();
            LinkBlocks();
            if (first)
            {
                LoadArt();
                if (App.I.Current == this) { FocusDefault(); }
            }
            else if (focusIndex >= 0 && App.I.Current == this && _buttons.GetChildCount() > 0)
            {
                ((Control)_buttons.GetChild(Math.Min(focusIndex, _buttons.GetChildCount() - 1))).GrabFocus();
            }

            if (_item.Type == "Series")
            {
                if (!_built) { BuildSeasons(); }
                await LoadEpisodesAsync(first);
            }
            _built = true;
        }
        catch (JellyfinException e) { App.I.Toast(e.Message); }
    }

    private int FocusedButtonIndex()
    {
        var owner = GetViewport().GuiGetFocusOwner();
        return _buttons != null && owner != null && owner.GetParent() == _buttons ? owner.GetIndex() : -1;
    }

    private async void LoadArt()
    {
        var api = Jellyfin.I;
        var poster = await ImageCache.I.GetAsync(_item.IsEpisode ? api.LandscapeUrl(_item, 640, 360) : api.PosterUrl(_item, 480, 720));
        if (!IsInstanceValid(this)) { return; }
        _poster.Texture = poster;
        if (_item.IsEpisode) { _poster.CustomMinimumSize = new Vector2(560, 315); }
        if (!Config.I.Backdrops) { return; }
        var backdrop = await ImageCache.I.GetAsync(api.BackdropUrl(_item));
        if (IsInstanceValid(this)) { _backdrop.Texture = backdrop; }
    }

    private void BuildInfo()
    {
        foreach (var child in _info.GetChildren())
        {
            _info.RemoveChild(child);
            child.QueueFree();
        }

        if (_item.IsEpisode) { _info.AddChild(Ui.Label(_item.SeriesName, 30, variation: "CaptionLabel")); }
        var title = Ui.Label(_item.IsEpisode ? $"{_item.EpisodeCode}  {_item.Name}" : _item.Name, 60);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _info.AddChild(title);

        var meta = new List<string>();
        if (_item.ProductionYear != null) { meta.Add(_item.ProductionYear.ToString()); }
        if (_item.RuntimeText != "") { meta.Add(_item.RuntimeText); }
        if (!string.IsNullOrEmpty(_item.OfficialRating)) { meta.Add(_item.OfficialRating); }
        if (_item.CommunityRating != null) { meta.Add($"★ {_item.CommunityRating:0.0}"); }
        if (_item.Type == "Series" && _seasons.Count > 0) { meta.Add(_seasons.Count == 1 ? "1 season" : $"{_seasons.Count} seasons"); }
        if (_item.Genres is { Count: > 0 }) { meta.Add(string.Join(", ", _item.Genres.Take(3))); }
        _info.AddChild(Ui.Label(string.Join("   ·   ", meta), 26, variation: "CaptionLabel"));

        if (_item.Taglines is { Count: > 0 }) { _info.AddChild(Ui.Label(_item.Taglines[0], 28, variation: "AccentLabel")); }

        var overview = Ui.Label(_item.Overview ?? "", 26);
        overview.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        overview.MaxLinesVisible = 6;
        overview.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        overview.CustomMinimumSize = new Vector2(600, 0);
        _info.AddChild(overview);
        _info.AddChild(Ui.Spacer(0, 6));

        _buttons = new HBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 16);
        _info.AddChild(_buttons);

        var target = _playTarget;
        if (target != null)
        {
            var resume = target.ResumeTicks > 0;
            var suffix = _item.Type == "Series" ? $" {target.EpisodeCode}" : "";
            // The chosen version applies when the page's own title is what plays.
            var version = target == _item ? _versionId : null;
            _buttons.AddChild(Ui.ActionButton((resume ? "Resume" : "Play") + suffix, () => App.I.Push(new PlayerScreen(target, false, version))));
            if (resume) { _buttons.AddChild(Ui.ActionButton("From Beginning", () => App.I.Push(new PlayerScreen(target, true, version)))); }
            if (target == _item && _item.MediaSources is { Count: > 1 } versions)
            {
                var current = versions.FirstOrDefault(v => v.Id == _versionId) ?? versions[0];
                _buttons.AddChild(Ui.ActionButton($"Version: {current.Name}", () =>
                {
                    var items = versions.Select(v => (v.Name ?? v.Id, (Action)(() =>
                    {
                        _versionId = v.Id;
                        BuildInfo();
                        LinkBlocks();
                        FocusDefault();
                    }))).ToList();
                    App.I.ShowMenu("Version", items, Math.Max(0, versions.IndexOf(current)));
                }));
            }
        }
        var data = _item.UserData;
        if (data != null)
        {
            _buttons.AddChild(Ui.ActionButton(data.Played ? "Mark Unwatched" : "Mark Watched", () => _ = ToggleAsync(true)));
            _buttons.AddChild(Ui.ActionButton(data.IsFavorite ? "Remove Favorite" : "Favorite", () => _ = ToggleAsync(false)));
        }
        if (_item.IsEpisode && _item.SeriesId != null)
        {
            _buttons.AddChild(Ui.ActionButton("Go to Series", () =>
                App.I.Push(new DetailsScreen(new BaseItem { Id = _item.SeriesId, Name = _item.SeriesName, Type = "Series" }))));
        }
    }

    private async Task ToggleAsync(bool played)
    {
        try
        {
            if (played) { await Jellyfin.I.SetPlayedAsync(_item.Id, !_item.UserData.Played); }
            else { await Jellyfin.I.SetFavoriteAsync(_item.Id, !_item.UserData.IsFavorite); }
            await LoadAsync(false);
        }
        catch (JellyfinException e) { App.I.Toast(e.Message); }
    }

    // ---- Series ----

    private void BuildSeasons()
    {
        _seasonBar = new HBoxContainer();
        _seasonBar.AddThemeConstantOverride("separation", 12);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var seasons = Ui.Pad(_seasonBar, Ui.Margin, 6, Ui.Margin, 6);
        scroll.AddChild(seasons);
        FocusScroller.KeepVisible(scroll, seasons, Ui.Margin, vertical: false);
        _below.AddChild(scroll);
        foreach (var season in _seasons)
        {
            var button = Ui.ActionButton(season.Name, () => SelectSeason(season.Id));
            button.SetMeta("season", season.Id);
            _seasonBar.AddChild(button);
        }
        _episodeHolder = new VBoxContainer();
        _below.AddChild(_episodeHolder);
        MarkSeason();
        LinkBlocks();
    }

    /// <summary>
    /// Points Up from the seasons at the action buttons and Down from the buttons at the seasons.
    /// Whichever block is scrolled out of view is clipped, and Godot skips clipped controls.
    /// </summary>
    private void LinkBlocks()
    {
        if (_seasonBar == null || _seasonBar.GetChildCount() == 0 || _buttons == null || _buttons.GetChildCount() == 0) { return; }
        var firstButton = (Control)_buttons.GetChild(0);
        var season = (Control)_seasonBar.GetChild(0);
        foreach (var child in _seasonBar.GetChildren())
        {
            var button = (Button)child;
            button.FocusNeighborTop = firstButton.GetPath();
            if (button.GetMeta("season").AsString() == _seasonId) { season = button; }
        }
        foreach (var child in _buttons.GetChildren()) { ((Control)child).FocusNeighborBottom = season.GetPath(); }
    }

    private void MarkSeason()
    {
        foreach (var child in _seasonBar.GetChildren())
        {
            var button = (Button)child;
            var selected = button.GetMeta("season").AsString() == _seasonId;
            button.AddThemeStyleboxOverride("normal", Ui.Box(selected ? Ui.AccentDim : Ui.Surface, 10, padX: 18, padY: 8));
        }
    }

    private void SelectSeason(string seasonId)
    {
        if (seasonId == _seasonId) { return; }
        _seasonId = seasonId;
        MarkSeason();
        _ = LoadEpisodesAsync(false);
    }

    private async Task LoadEpisodesAsync(bool first)
    {
        if (_seasonId == null) { return; }
        var seasonId = _seasonId;
        var episodes = await Jellyfin.I.EpisodesAsync(_item.Id, seasonId);
        if (!IsInstanceValid(this) || seasonId != _seasonId) { return; }

        var focused = GetViewport().GuiGetFocusOwner() as Card;
        var focusedId = focused != null && _episodeHolder.IsAncestorOf(focused) ? focused.Item.Id : null;
        foreach (var child in _episodeHolder.GetChildren())
        {
            _episodeHolder.RemoveChild(child);
            child.QueueFree();
        }
        if (episodes.Count == 0) { return; }
        var row = new CardRow("Episodes", episodes, CardShape.Landscape, 400, card => App.I.Open(card.Item, true), true, true);
        _episodeHolder.AddChild(row);
        if (focusedId != null && App.I.Current == this)
        {
            (row.Cards.FirstOrDefault(c => c.Item.Id == focusedId) ?? row.Cards[0]).GrabFocus();
        }
    }
}
