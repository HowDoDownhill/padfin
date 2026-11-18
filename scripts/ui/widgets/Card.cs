namespace Padfin;

/// <summary>
/// A FishUI card for a library item: artwork, title, subtitle, and watch progress. It carries the
/// Jellyfin item or Seerr title it shows, and takes its size and text from Padfin's settings.
/// </summary>
public partial class Card : FishUI.Card
{
    /// <summary>The Jellyfin item, or null for a card showing a Seerr title.</summary>
    public BaseItem Item { get; private set; }

    /// <summary>The Seerr title, or null for a card showing a Jellyfin item.</summary>
    public SeerrItem Discovered { get; private set; }

    /// <summary>Starts playback on A when true. Opens details when false.</summary>
    public bool PlayOnAccept;

    public Card() { }

    /// <param name="withinSeries">True in a season's episode list, where the series name would repeat on every card.</param>
    public Card(BaseItem item, CardShape shape, int width, bool deferImage = false, bool withinSeries = false)
    {
        Item = item;
        var title = item.IsEpisode && shape == CardShape.Landscape ? item.SeriesName ?? item.Name : item.Name;
        var subtitle = Subtitle(item, shape);
        if (withinSeries && item.IsEpisode)
        {
            title = item.IndexNumber != null ? $"{item.IndexNumber}. {item.Name}" : item.Name;
            subtitle = item.RuntimeText;
        }
        var data = item.UserData;
        var progress = data?.PlayedPercentage is > 0 and < 100 ? data.PlayedPercentage.Value : 0;
        var badge = data == null ? null : data.Played ? "✓" : data.UnplayedItemCount is > 0 ? data.UnplayedItemCount.ToString() : null;
        var url = shape == CardShape.Poster ? Jellyfin.I.PosterUrl(item) : Jellyfin.I.LandscapeUrl(item);
        Build(title, subtitle, shape, width, url, progress, badge, deferImage);
    }

    /// <summary>A title from Seerr. The badge shows whether it is in the library or already requested.</summary>
    public Card(SeerrItem item, int width, string subtitle = null)
    {
        Discovered = item;
        var badge = item.MediaInfo?.Status switch
        {
            SeerrMedia.Available => "✓",
            SeerrMedia.PartiallyAvailable => "½",
            SeerrMedia.Pending or SeerrMedia.Processing => "…",
            _ => null,
        };
        subtitle ??= $"{(item.IsMovie ? "Movie" : "TV")} · {item.Year}".Trim(' ', '·');
        Build(item.DisplayTitle, subtitle, CardShape.Poster, width, Seerr.I.PosterUrl(item), 0, badge, false);
    }

    private void Build(string title, string subtitle, CardShape shape, int width, string url, double progress, string badge, bool deferImage)
    {
        width = (int)(width * Config.I.CardScale); // The card size chosen in settings.
        // Settings can hide the text for a denser wall of artwork.
        Build(title, subtitle, shape, width, progress, badge, Config.I.CardTitles);
        if (url != null) { ImageSource = async () => await ImageCache.I.GetAsync(url); }
        if (!deferImage) { LoadImage(); }
    }

    private static string Subtitle(BaseItem item, CardShape shape)
    {
        if (item.IsEpisode)
        {
            var code = item.EpisodeCode;
            return shape == CardShape.Landscape ? $"{code} · {item.Name}".Trim(' ', '·') : $"{item.SeriesName} {code}".Trim();
        }
        if (item.Type == "Series" && item.ProductionYear != null) { return item.ProductionYear.ToString(); }
        if (item.Type == "Season") { return item.SeriesName ?? ""; }
        if (item.IsContainer) { return item.ChildCount is > 0 ? $"{item.ChildCount} items" : ""; }
        return item.ProductionYear?.ToString() ?? "";
    }
}
