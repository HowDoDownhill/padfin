using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>Home: libraries, Continue Watching, Next Up, and the latest additions to each library.</summary>
public partial class HomeScreen : Screen
{
    private RowPage _page;
    private HeroBanner _banner;
    private Card _bannerCard;
    private Label _status;
    private bool _loading;

    public override string Tab => "home";
    public override string Title => "Home";

    public override IEnumerable<(string, string)> Hints =>
        new[] { (Actions.Accept, "Select"), (Actions.X, "Details"), (Actions.Y, "Search") };

    public override void OnEnter()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        Ui.Fill(column);
        AddChild(column);

        // Hidden until a card has focus, so the loading message sits at the top.
        _banner = new HeroBanner { Visible = false };
        column.AddChild(_banner);

        _page = new RowPage();
        _page.CardFocused += (row, card) => ShowInBanner(row.Title, card as Card);
        column.AddChild(_page);
        _status = _page.ShowMessage("Loading…");

        _ = LoadAsync();
    }

    public override void OnResume() => _ = LoadAsync();

    public override void FocusDefault() => _page.Focus();

    private async Task LoadAsync()
    {
        if (_loading) { return; }
        _loading = true;
        try
        {
            var api = Jellyfin.I;
            var viewsTask = api.ViewsAsync();
            var resumeTask = api.ResumeAsync();
            var nextUpTask = api.NextUpAsync();
            var views = await viewsTask;
            var resume = await resumeTask;
            var nextUp = await nextUpTask;

            var latestViews = views.Where(v => v.CollectionType is "movies" or "tvshows" or null or "mixed" or "homevideos").ToList();
            var latest = await Task.WhenAll(latestViews.Select(v => SafeLatest(v.Id)));
            if (!IsInstanceValid(this)) { return; }

            // Remember which card had focus so a refresh after playback keeps the user's place.
            var (focusRow, focusCard) = _page.FocusedPosition();
            _page.Clear();

            // Settings choose which sections appear.
            var config = Config.I;
            if (config.HomeContinueWatching) { AddRow("Continue Watching", resume, CardShape.Landscape, 380, true); }
            if (config.HomeNextUp) { AddRow("Next Up", nextUp, CardShape.Landscape, 380, true); }
            if (config.HomeLibraries) { AddRow("Libraries", views, CardShape.Landscape, 320, false); }
            for (var i = 0; config.HomeLatest && i < latestViews.Count; i++)
            {
                AddRow($"Latest in {latestViews[i].Name}", latest[i], CardShape.Poster, 220, false);
            }
            _page.Finish();

            if (_page.Rows.Count == 0) { _page.ShowMessage("Nothing to show. Home sections can be turned on in Settings."); }
            else if (App.I.Current == this) { _page.Focus(focusRow, focusCard); }
        }
        catch (JellyfinException e)
        {
            if (IsInstanceValid(this) && IsInstanceValid(_status)) { _status.Text = e.Message; }
            App.I.Toast(e.Message);
        }
        finally { _loading = false; }
    }

    /// <summary>
    /// Describes the focused card in the banner. The card's own artwork shows at once and the
    /// item's backdrop replaces it when it has loaded, if focus is still on that card.
    /// </summary>
    private async void ShowInBanner(string rowTitle, Card card)
    {
        var item = card?.Item;
        if (item == null) { return; }
        _bannerCard = card;
        _banner.Visible = true;
        _banner.Display(item.IsEpisode ? item.SeriesName ?? item.Name : item.Name, BannerLine(item), card.Image, rowTitle);

        if (!Config.I.Backdrops) { return; }
        var backdrop = await ImageCache.I.GetAsync(Jellyfin.I.BackdropUrl(item));
        if (backdrop != null && IsInstanceValid(this) && _bannerCard == card) { _banner.SetArt(backdrop); }
    }

    private static string BannerLine(BaseItem item)
    {
        var parts = item.IsEpisode
            ? new[] { item.EpisodeCode, item.Name, item.RuntimeText }
            : item.IsContainer
                ? new[] { item.ChildCount is > 0 ? $"{item.ChildCount} items" : "" }
                : new[] { item.ProductionYear?.ToString(), item.RuntimeText, item.OfficialRating };
        return string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part)));
    }

    private static async Task<List<BaseItem>> SafeLatest(string viewId)
    {
        try { return await Jellyfin.I.LatestAsync(viewId); }
        catch (JellyfinException) { return new List<BaseItem>(); }
    }

    private void AddRow(string title, List<BaseItem> items, CardShape shape, int width, bool playOnAccept)
    {
        if (items == null || items.Count == 0) { return; }
        _page.Add(new CardRow(title, items, shape, width, card => App.I.Open(card.Item, card.PlayOnAccept), playOnAccept));
    }

    public override bool OnInput(InputEvent e)
    {
        if (e.IsActionPressed(Actions.X))
        {
            if (GetViewport().GuiGetFocusOwner() is Card card) { App.I.Open(card.Item); }
            return true;
        }
        if (e.IsActionPressed(Actions.Y))
        {
            App.I.Push(new SearchScreen());
            return true;
        }
        return false;
    }

    public override MenuGroup PageMenu => new("Home", new (string, Action)[] { ("Refresh", () => _ = LoadAsync()) });
}
