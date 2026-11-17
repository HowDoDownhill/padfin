using Godot;

namespace Padfin;

public enum CardShape { Poster, Landscape }

/// <summary>
/// A focusable library tile: artwork, title, subtitle, and watch progress. It follows RomBox's game
/// card: a rounded dark panel with padded artwork, and when selected a drop shadow and the shared
/// selection highlight, which glides between cards.
/// </summary>
public partial class Card : Button
{
    private const int Padding = 12;
    private const int TextHeight = 62;
    private const int CornerRadius = 8;
    private const int SelectedCornerRadius = 10;
    private const int ShadowSize = 20;
    private const int ShadowDrop = 6;

    /// <summary>
    /// How far the selected card's shadow reaches above and below it. A scrolling container needs
    /// at least this much padding around its cards, or it clips the shadow to a hard edge.
    /// </summary>
    public const int ShadowAbove = ShadowSize - ShadowDrop + 2;
    public const int ShadowBelow = ShadowSize + ShadowDrop + 2;

    /// <summary>The Jellyfin item, or null for a card showing a Seerr title.</summary>
    public BaseItem Item { get; private set; }

    /// <summary>The Seerr title, or null for a card showing a Jellyfin item.</summary>
    public SeerrItem Discovered { get; private set; }
    /// <summary>Starts playback on A when true. Opens details when false.</summary>
    public bool PlayOnAccept;

    private TextureRect _image;
    private Panel _panel;
    private Panel _shadow;
    private string _url;
    private bool _requested;

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
        var imageSize = shape == CardShape.Poster ? new Vector2(width, width * 1.5f) : new Vector2(width, width * 9f / 16f);
        var showText = Config.I.CardTitles; // Settings can hide the text for a denser wall of artwork.
        CustomMinimumSize = imageSize + new Vector2(Padding * 2, Padding * 2 + (showText ? TextHeight : 0));
        FocusMode = FocusModeEnum.All;
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        _shadow = new Panel { Visible = false, ShowBehindParent = true, MouseFilter = MouseFilterEnum.Ignore };
        // The selection box has slightly rounder corners than the card, and the shadow sits under the box.
        var shadowStyle = Ui.Box(new Color(0, 0, 0, 0), SelectedCornerRadius);
        if (!Appearance.IsSimple)
        {
            // A soft translucent edge on the selected card: white on dark palettes, dark on light ones. Simple
            // marks the selection with its accent ring.
            shadowStyle.SetBorderWidthAll(2);
            shadowStyle.BorderColor = Appearance.IsLight ? new Color(0, 0, 0, 0.35f) : new Color(1, 1, 1, 0.45f);
        }
        shadowStyle.ShadowColor = new Color(0, 0, 0, Appearance.IsLight ? 0.2f : 0.45f);
        shadowStyle.ShadowSize = ShadowSize;
        shadowStyle.ShadowOffset = new Vector2(0, ShadowDrop);
        _shadow.AddThemeStyleboxOverride("panel", shadowStyle);
        AddChild(_shadow);
        Ui.Fill(_shadow);

        _panel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, Appearance.IsLight ? 0.07f : 0.25f), CornerRadius));
        AddChild(_panel);
        Ui.Fill(_panel);

        var origin = new Vector2(Padding, Padding);
        var initial = Ui.Label(string.IsNullOrEmpty(title) ? "" : title[..1].ToUpperInvariant(), 72, variation: "MutedLabel");
        initial.Position = origin;
        initial.Size = imageSize;
        initial.Modulate = new Color(1, 1, 1, 0.35f);
        initial.HorizontalAlignment = HorizontalAlignment.Center;
        initial.VerticalAlignment = VerticalAlignment.Center;
        AddChild(initial);

        _image = new TextureRect
        {
            Position = origin,
            Size = imageSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_image);

        if (showText)
        {
            AddChild(TextLine(title, 22, null, Padding + imageSize.Y + 6, width));
            AddChild(TextLine(subtitle, 19, "CaptionLabel", Padding + imageSize.Y + 34, width));
        }

        if (progress > 0)
        {
            var bar = new Vector2(Padding, Padding + imageSize.Y - 8);
            AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.6f), Position = bar, Size = new Vector2(width, 8), MouseFilter = MouseFilterEnum.Ignore });
            AddChild(new ColorRect { Color = Ui.Accent, Position = bar, Size = new Vector2(width * (float)(progress / 100.0), 8), MouseFilter = MouseFilterEnum.Ignore });
        }
        if (badge != null) { AddChild(Badge(badge, width)); }

        FocusEntered += () => SetSelected(true);
        FocusExited += () => SetSelected(false);

        _url = url;
        if (!deferImage) { LoadImage(); }
    }

    /// <summary>
    /// Under Glass the selected card gives up its own panel and the selection box becomes its body,
    /// at the card's exact size, so the two read as one object in the box's colour and opacity.
    /// Simple keeps the panel and draws its accent ring around it.
    /// </summary>
    private void SetSelected(bool selected)
    {
        _shadow.Visible = selected;
        _panel.Visible = !selected || Appearance.IsSimple;
        SelectionHighlighter.Set(this, this, selected, 0, FocusHighlight.RingGap);
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

    private static Label TextLine(string text, int size, string variation, float y, int width)
    {
        // Clipping goes first: an unclipped label refuses to be narrower than its text.
        var label = Ui.Label(text, size, variation: variation);
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.Position = new Vector2(Padding, y);
        label.Size = new Vector2(width, size + 10);
        return label;
    }

    private static Control Badge(string text, int width)
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0.7f), 14, 2, Ui.Accent, 10, 2));
        panel.AddChild(Ui.Label(text, 18, Colors.White));
        panel.Position = new Vector2(Padding + width - 8 - (24 + text.Length * 11), Padding + 8);
        return panel;
    }

    /// <summary>Requests the artwork. Safe to call more than once.</summary>
    public async void LoadImage()
    {
        if (_requested || _url == null) { return; }
        _requested = true;
        var texture = await ImageCache.I.GetAsync(_url);
        if (!IsInstanceValid(this) || !_requested) { return; }
        _image.Texture = texture;
    }

    /// <summary>Drops the artwork so a long grid does not keep every poster in video memory.</summary>
    public void UnloadImage()
    {
        if (!_requested) { return; }
        _requested = false;
        _image.Texture = null;
    }
}
