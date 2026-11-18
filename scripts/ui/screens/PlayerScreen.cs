using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Padfin.Player;

namespace Padfin;

/// <summary>Video playback: mpv fills the screen and Godot draws the controller overlay on top.</summary>
public partial class PlayerScreen : Screen
{
    private const long TicksPerSecond = 10_000_000;
    private const double OverlaySeconds = 4;
    private const double ScrubCommitSeconds = 0.8;
    private const double UpNextSeconds = 20;
    private const int MaxThumbnailSheets = 4;
    /// <summary>mpv sub-pos that keeps subtitles above the control bar. 100 is the bottom of the picture.</summary>
    private const int SubtitlePositionAboveControls = 76;

    private readonly BaseItem _item;
    private readonly bool _fromStart;
    private readonly string _rawUrl;
    private readonly string _versionId;

    private MpvPlayer _mpv;
    private TextureRect _video;
    private Control _overlay;
    private Label _title;
    private Label _subtitle;
    private Label _elapsed;
    private Label _remaining;
    private Label _status;
    private SeekBar _bar;
    private PanelContainer _prompt;
    private Button _promptButton;
    private PanelContainer _stats;
    private Label _statsLabel;
    private PanelContainer _preview;
    private TextureRect _previewImage;
    private Label _previewTime;
    private double _statsIn;
    private double _playingFor;
    private bool _decoderLogged;

    // The stream being played.
    private string _mediaSourceId;
    private string _playSessionId;
    private bool _transcoding;
    private bool _triedTranscode;
    private MediaSource _source;
    private List<MediaStream> _externalSubtitles = new();
    private int? _audioIndex;
    private int? _subtitleIndex;

    // What the server knows about the title.
    private List<MediaSegment> _segments = new();
    private readonly HashSet<MediaSegment> _autoSkipped = new();
    private List<Chapter> _chapters = new();
    private TrickplayInfo _thumbnails;
    private readonly Dictionary<int, Task<ImageTexture>> _sheets = new();
    private readonly List<int> _sheetOrder = new();
    private BaseItem _next;

    // What the prompt button in the corner currently offers.
    private MediaSegment _activeSegment;
    private bool _upNext;

    private bool _loaded;
    private bool _closing;
    private bool _wasPaused;
    private bool? _overlayShown;
    private double _overlayFor;
    private double _reportIn;
    private double _position;
    private double _duration;
    private double? _scrubTo;
    private double _scrubIdle;
    private int _previewWanted = -1;

    public override bool ShowChrome => false;

    public PlayerScreen() { }

    /// <param name="versionId">The media source to play, for titles with more than one version.</param>
    public PlayerScreen(BaseItem item, bool fromStart, string versionId = null)
    {
        _item = item;
        _fromStart = fromStart;
        _versionId = versionId;
    }

    /// <summary>Plays a file or URL directly, with no Jellyfin session. A developer aid.</summary>
    public PlayerScreen(string rawUrl) { _rawUrl = rawUrl; }

    public override void OnEnter()
    {
        var black = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
        Ui.Fill(black);
        AddChild(black);

        _video = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Ui.Fill(_video);
        AddChild(_video);

        BuildOverlay();
        DisplayServer.ScreenSetKeepOn(true);
        UiSounds.Muted = true;
        GetViewport().GuiReleaseFocus();

        try
        {
            _mpv = new MpvPlayer();
            _mpv.FileLoaded += OnFileLoaded;
            _mpv.Ended += OnEnded;
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or BadImageFormatException)
        {
            _status.Text = e.Message;
            ShowOverlay();
            return;
        }
        _ = BeginAsync();
    }

    private void BuildOverlay()
    {
        _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        Ui.Fill(_overlay);
        AddChild(_overlay);

        var top = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        top.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        top.OffsetBottom = 150;
        _overlay.AddChild(top);
        var titles = new VBoxContainer();
        _title = Ui.Label(_item?.SeriesName ?? _item?.Name ?? "Padfin", 40);
        _subtitle = Ui.Label(_item is { IsEpisode: true } ? $"{_item.EpisodeCode}  {_item.Name}" : "", 26, variation: "CaptionLabel");
        titles.AddChild(_title);
        titles.AddChild(_subtitle);
        var titlePad = Ui.Pad(titles, Ui.Margin, 34, Ui.Margin, 0);
        titlePad.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _overlay.AddChild(titlePad);

        var bottom = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        bottom.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        bottom.OffsetTop = -190;
        _overlay.AddChild(bottom);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        _status = Ui.Label("Loading…", 26, variation: "CaptionLabel");
        column.AddChild(_status);
        _bar = new SeekBar();
        column.AddChild(_bar);
        var times = new HBoxContainer();
        _elapsed = Ui.Label("0:00", 24);
        _remaining = Ui.Label("", 24, variation: "CaptionLabel");
        times.AddChild(_elapsed);
        times.AddChild(Ui.Spacer(expand: true));
        foreach (var (action, label) in new[] { (Actions.Accept, "Pause"), (Actions.X, "Audio"), (Actions.Y, "Subtitles"), (Actions.Start, "More"), (Actions.Back, "Stop") })
        {
            times.AddChild(Ui.Hint(action, label, 22));
            times.AddChild(Ui.Spacer(10));
        }
        times.AddChild(Ui.Spacer(expand: true));
        times.AddChild(_remaining);
        column.AddChild(times);
        var pad = Ui.Pad(column, Ui.Margin, 0, Ui.Margin, 30);
        pad.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        pad.OffsetTop = -170;
        pad.MouseFilter = MouseFilterEnum.Ignore;
        _overlay.AddChild(pad);

        // The thumbnail and time that follow the knob while scrubbing.
        var previewColumn = new VBoxContainer();
        previewColumn.AddThemeConstantOverride("separation", 6);
        _previewImage = new TextureRect
        {
            CustomMinimumSize = new Vector2(320, 180),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        previewColumn.AddChild(_previewImage);
        _previewTime = Ui.Label("", 28, Colors.White);
        _previewTime.HorizontalAlignment = HorizontalAlignment.Center;
        previewColumn.AddChild(_previewTime);
        _preview = Ui.MicaPanel(previewColumn, 10, 8);
        _preview.Visible = false;
        _preview.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_preview);

        _statsLabel = Ui.Label("", 20, Colors.White);
        _stats = Ui.MicaPanel(_statsLabel, 18, 12);
        _stats.Visible = Config.I.PlaybackStats;
        _stats.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _stats.GrowHorizontal = GrowDirection.Begin;
        _stats.OffsetRight = -Ui.Margin;
        _stats.OffsetTop = 40;
        AddChild(_stats);

        _promptButton = Ui.Hint(Actions.Accept, "Skip Intro", 28);
        _prompt = Ui.MicaPanel(_promptButton, 22, 8);
        _prompt.Visible = false;
        _prompt.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        _prompt.GrowHorizontal = GrowDirection.Begin;
        _prompt.GrowVertical = GrowDirection.Begin;
        _prompt.OffsetRight = -Ui.Margin;
        _prompt.OffsetBottom = -220;
        AddChild(_prompt);
    }

    // ---- Starting a stream ----

    private async Task BeginAsync()
    {
        if (_rawUrl != null)
        {
            _title.Text = System.IO.Path.GetFileName(_rawUrl);
            _mpv.Load(_rawUrl, 0, null);
            return;
        }

        var api = Jellyfin.I;
        await api.LoadPreferencesAsync();
        var start = _fromStart ? 0 : _item.ResumeTicks / (double)TicksPerSecond;
        if (start > 0) { start = Math.Max(0, start - Config.I.ResumeRewindSeconds); }
        if (!await StartStreamAsync(start, false) || _closing) { return; }

        // The rest is not needed to start the picture, so it loads while the video does.
        var segments = api.SegmentsAsync(_item.Id);
        var details = LoadDetailsAsync();
        if (_item.IsEpisode && _item.SeriesId != null)
        {
            try
            {
                var following = await api.EpisodesAsync(_item.SeriesId, startItemId: _item.Id, limit: 2);
                _next = following.Count > 1 && following[0].Id == _item.Id ? following[1] : null;
            }
            catch (JellyfinException) { }
        }
        try { _segments = await segments; } catch (JellyfinException) { }
        await details;
    }

    /// <summary>
    /// Asks the server how to play the title and starts it at a position. Used for the first start,
    /// for a retry as a converted stream, and when the quality is changed mid-video.
    /// </summary>
    private async Task<bool> StartStreamAsync(double startSeconds, bool forceTranscode)
    {
        try
        {
            var api = Jellyfin.I;
            _loaded = false;
            _status.Text = "Loading…";
            var info = await api.PlaybackInfoAsync(_item.Id, forceTranscode, _versionId);
            if (_closing) { return false; }
            var source = info?.MediaSources?.FirstOrDefault(s => s.Id == _versionId) ?? info?.MediaSources?.FirstOrDefault();
            if (source == null)
            {
                Fail(info?.ErrorCode != null ? $"The server cannot play this: {info.ErrorCode}." : "The server has no playable version of this.");
                return false;
            }
            _mediaSourceId = source.Id;
            _playSessionId = info.PlaySessionId ?? Guid.NewGuid().ToString("N");

            var direct = !forceTranscode && (source.SupportsDirectPlay || source.SupportsDirectStream || source.TranscodingUrl == null);
            if (!direct && source.TranscodingUrl == null)
            {
                Fail("The server could not convert this video.");
                return false;
            }
            _transcoding = !direct;
            _source = direct ? source : null;
            _externalSubtitles = direct
                ? source.MediaStreams.Where(s => s.Type == "Subtitle" && s.IsExternal && s.IsTextSubtitleStream).ToList()
                : new List<MediaStream>();
            _audioIndex = source.DefaultAudioStreamIndex;
            _subtitleIndex = source.DefaultSubtitleStreamIndex ?? -1;

            var url = direct ? api.DirectStreamUrl(_item.Id, source.Id, _playSessionId) : api.Server + source.TranscodingUrl;
            var (audio, subtitle) = direct ? ServerTracks() : ("auto", "auto");
            _mpv.Load(url, startSeconds, api.StreamAuthHeader, audio, subtitle);
            return true;
        }
        catch (JellyfinException e)
        {
            Fail(e.Message);
            return false;
        }
    }

    /// <summary>Chapters and scrubbing thumbnails come with the title's full details.</summary>
    private async Task LoadDetailsAsync()
    {
        try
        {
            var full = await Jellyfin.I.ItemAsync(_item.Id);
            if (_closing || full == null) { return; }
            _chapters = full.Chapters ?? new List<Chapter>();

            // The largest thumbnails up to 400 pixels wide: clear at preview size without heavy sheets.
            if (full.Trickplay != null && _mediaSourceId != null && full.Trickplay.TryGetValue(_mediaSourceId, out var sizes) && sizes.Count > 0)
            {
                var usable = sizes.Values.Where(t => t.Width > 0 && t.Interval > 0 && t.TileWidth > 0 && t.TileHeight > 0).OrderBy(t => t.Width).ToList();
                _thumbnails = usable.LastOrDefault(t => t.Width <= 400) ?? usable.FirstOrDefault();
            }
            ApplyChapterMarks();
        }
        catch (JellyfinException) { }
    }

    private void ApplyChapterMarks()
    {
        if (_duration <= 0) { return; }
        _bar.Marks = _chapters.Select(c => (float)(c.StartPositionTicks / (double)TicksPerSecond / _duration)).ToList();
    }

    private void Fail(string message)
    {
        if (_closing) { return; }
        App.I.Toast(message);
        _ = CloseAsync();
    }

    private void OnFileLoaded()
    {
        _loaded = true;
        _status.Text = "";
        foreach (var stream in _externalSubtitles)
        {
            _mpv.AddSubtitle(Jellyfin.I.ExternalSubtitleUrl(_item.Id, _mediaSourceId, stream), stream.DisplayTitle ?? stream.Title, stream.Language,
                select: stream.Index == _source?.DefaultSubtitleStreamIndex);
        }
        ShowOverlay();
        if (_item != null) { _ = Report(Jellyfin.I.ReportStartAsync, null); }
        _reportIn = 10;
    }

    /// <summary>
    /// The audio and subtitle tracks the server chose from the user's preferences, the way the web
    /// client gets them, as mpv track numbers. mpv numbers tracks per kind in file order.
    /// A subtitle that lives outside the file is selected later, when it is added.
    /// </summary>
    private (string Audio, string Subtitle) ServerTracks()
    {
        var audioAt = EmbeddedStreams("Audio").FindIndex(s => s.Index == _source.DefaultAudioStreamIndex);
        var subtitleAt = EmbeddedStreams("Subtitle").FindIndex(s => s.Index == _source.DefaultSubtitleStreamIndex);
        return (audioAt >= 0 ? (audioAt + 1).ToString() : "auto", subtitleAt >= 0 ? (subtitleAt + 1).ToString() : "no");
    }

    private List<MediaStream> EmbeddedStreams(string type) =>
        _source.MediaStreams.Where(s => s.Type == type && !s.IsExternal).OrderBy(s => s.Index).ToList();

    private void OnEnded(bool error)
    {
        if (_closing) { return; }
        if (error)
        {
            // A file mpv cannot open as it is gets one more try as a stream the server converts.
            if (_item != null && !_transcoding && !_triedTranscode)
            {
                _triedTranscode = true;
                App.I.Toast("Direct play failed. Trying a converted stream.");
                _ = StartStreamAsync(_position, true);
                return;
            }
            Fail("Playback failed.");
            return;
        }
        if (_item != null) { _position = _duration; }
        if (_next != null && AutoPlayNext) { _ = CloseAsync(_next); }
        else { _ = CloseAsync(); }
    }

    private static bool AutoPlayNext => Jellyfin.I.Preference("EnableNextEpisodeAutoPlay", true);

    // ---- Frame loop ----

    public override void _Process(double delta)
    {
        if (_mpv == null || _closing) { return; }
        _mpv.Update(DisplayServer.WindowGetSize());
        if (_mpv == null || _closing) { return; }
        if (_video.Texture != _mpv.Texture) { _video.Texture = _mpv.Texture; }
        if (!_loaded) { return; }

        // Logged a moment into playback. mpv has not settled on a decoder when the file first opens.
        _playingFor += delta;
        if (!_decoderLogged && _playingFor > 2)
        {
            _decoderLogged = true;
            var decoder = _mpv.GetString("hwdec-current");
            GD.Print($"Playing with {(_mpv.IsGpuRendered ? "GPU" : "software")} rendering, decoder: {(string.IsNullOrEmpty(decoder) || decoder == "no" ? "software" : decoder)}");
        }

        var position = _mpv.Position;
        var duration = _mpv.Duration;
        if (duration > 0)
        {
            if (_duration <= 0) { Callable.From(ApplyChapterMarks).CallDeferred(); }
            _position = position;
            _duration = duration;
        }
        var paused = _mpv.Paused;
        var buffering = !paused && _mpv.Buffering;

        // The server hears about a pause straight away, so other devices show the right state.
        if (paused != _wasPaused)
        {
            _wasPaused = paused;
            if (_item != null) { _ = Report(Jellyfin.I.ReportProgressAsync, paused ? "pause" : "unpause"); }
        }

        UpdateScrub(delta);

        _bar.Value = duration > 0 ? (float)(position / duration) : 0;
        _elapsed.Text = Ui.FormatTime(position);
        _remaining.Text = duration > 0 ? $"-{Ui.FormatTime(duration - position)}  ·  ends {EndsAt(duration - position)}" : "";
        _status.Text = paused ? "Paused" : buffering ? "Buffering…" : "";

        if (paused || buffering || _scrubTo != null) { _overlayFor = OverlaySeconds; }
        _overlayFor -= delta;
        SetOverlayVisible(_overlayFor > 0);

        UpdatePrompt(position, duration);
        UpdateStats(delta);

        _reportIn -= delta;
        if (_reportIn <= 0 && _item != null)
        {
            _reportIn = 10;
            _ = Report(Jellyfin.I.ReportProgressAsync, "timeupdate");
        }
    }

    /// <summary>The clock time playback will finish at the current speed.</summary>
    private string EndsAt(double remainingSeconds)
    {
        var speed = Math.Max(0.1, _mpv.Speed);
        var format = Config.I.Clock switch { "12" => "h:mm tt", "24" => "HH:mm", _ => "t" };
        return DateTime.Now.AddSeconds(remainingSeconds / speed).ToString(format);
    }

    private void ShowOverlay() => _overlayFor = OverlaySeconds;

    /// <summary>Shows or hides the controls, lifting subtitles clear of them while they are up.</summary>
    private void SetOverlayVisible(bool visible)
    {
        // Compared with the last state applied here. The overlay starts out visible on its own.
        if (_overlayShown == visible) { return; }
        _overlayShown = visible;
        _overlay.Visible = visible;
        var position = visible ? Math.Min(Config.I.SubtitlePosition, SubtitlePositionAboveControls) : Config.I.SubtitlePosition;
        _mpv.SetProperty("sub-pos", position.ToString());
    }

    /// <summary>Refreshes the playback info panel twice a second while it is showing.</summary>
    private void UpdateStats(double delta)
    {
        if (!_stats.Visible) { return; }
        _statsIn -= delta;
        if (_statsIn > 0) { return; }
        _statsIn = 0.5;
        var decoder = _mpv.GetString("hwdec-current");
        var lines = new List<string>
        {
            _transcoding ? "Transcoding on the server" : "Direct play",
            $"Video  {(_mpv.GetString("video-format") ?? "?").ToUpperInvariant()}  {_mpv.GetDouble("video-params/w"):0}×{_mpv.GetDouble("video-params/h"):0}  {_mpv.GetDouble("estimated-vf-fps"):0.##} fps",
            $"Decoder  {(string.IsNullOrEmpty(decoder) || decoder == "no" ? "software" : decoder)}",
            $"Audio  {(_mpv.GetString("audio-codec-name") ?? "?").ToUpperInvariant()}  {_mpv.GetDouble("audio-params/channel-count"):0} channels",
            $"Dropped frames  {_mpv.GetDouble("frame-drop-count"):0}",
            $"Buffered  {_mpv.GetDouble("demuxer-cache-duration"):0.0} s",
        };
        var bitrate = _mpv.GetDouble("video-bitrate");
        if (bitrate > 0) { lines.Insert(2, $"Bitrate  {bitrate / 1_000_000:0.0} Mbps"); }
        _statsLabel.Text = string.Join("\n", lines);
    }

    // ---- Scrubbing ----

    /// <summary>
    /// Moves the seek target without seeking yet. The knob, a thumbnail and the time show where it
    /// would land. It commits after a short pause or on A, so several presses make one seek.
    /// </summary>
    private void Scrub(double seconds)
    {
        if (_duration <= 0)
        {
            _mpv.SeekRelative(seconds);
            return;
        }
        _scrubTo = Math.Clamp((_scrubTo ?? _position) + seconds, 0, Math.Max(0, _duration - 1));
        _scrubIdle = ScrubCommitSeconds;
    }

    private void UpdateScrub(double delta)
    {
        if (_scrubTo == null)
        {
            _bar.Preview = null;
            _preview.Visible = false;
            return;
        }
        _scrubIdle -= delta;
        if (_scrubIdle <= 0)
        {
            CommitScrub();
            return;
        }

        var target = _scrubTo.Value;
        var fraction = (float)(target / _duration);
        _bar.Preview = fraction;
        _previewTime.Text = Ui.FormatTime(target);
        ShowThumbnail(target);

        // The preview rides above the knob and stays inside the screen edges.
        _preview.Visible = true;
        var bar = _bar.GetGlobalRect();
        var size = _preview.Size;
        var x = Math.Clamp(bar.Position.X + bar.Size.X * fraction - size.X / 2, Ui.Margin, Size.X - Ui.Margin - size.X);
        _preview.GlobalPosition = new Vector2(x, bar.Position.Y - size.Y - 18);
    }

    private void CommitScrub()
    {
        if (_scrubTo is not { } target) { return; }
        _scrubTo = null;
        _position = target;
        _mpv.SeekTo(target);
        _bar.Preview = null;
        _preview.Visible = false;
    }

    /// <summary>Shows the scrubbing thumbnail nearest a time, when the server has them for this title.</summary>
    private async void ShowThumbnail(double seconds)
    {
        if (_thumbnails == null || _transcoding)
        {
            _previewImage.Visible = false;
            return;
        }
        var info = _thumbnails;
        var index = Math.Clamp((int)(seconds * 1000 / info.Interval), 0, Math.Max(0, info.ThumbnailCount - 1));
        if (index == _previewWanted) { return; }
        _previewWanted = index;

        var perSheet = info.TileWidth * info.TileHeight;
        var sheet = await SheetAsync(index / perSheet);
        if (!IsInstanceValid(this) || _previewWanted != index) { return; }
        if (sheet == null)
        {
            _previewImage.Visible = false;
            return;
        }
        var cell = index % perSheet;
        _previewImage.Texture = new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2(cell % info.TileWidth * info.Width, cell / info.TileWidth * info.Height, info.Width, info.Height),
        };
        _previewImage.CustomMinimumSize = new Vector2(320, 320f * info.Height / info.Width);
        _previewImage.Visible = true;
    }

    /// <summary>One sheet of thumbnails. Only the few most recent sheets are kept, since each is a large texture.</summary>
    private Task<ImageTexture> SheetAsync(int sheet)
    {
        if (_sheets.TryGetValue(sheet, out var known)) { return known; }
        var task = ImageCache.I.LoadUncachedAsync(Jellyfin.I.TrickplayUrl(_item.Id, _mediaSourceId, _thumbnails.Width, sheet));
        _sheets[sheet] = task;
        _sheetOrder.Add(sheet);
        while (_sheetOrder.Count > MaxThumbnailSheets)
        {
            _sheets.Remove(_sheetOrder[0]);
            _sheetOrder.RemoveAt(0);
        }
        return task;
    }

    // ---- Skip and Up Next prompt ----

    private void UpdatePrompt(double position, double duration)
    {
        var ticks = (long)(position * TicksPerSecond);
        var segment = _segments.FirstOrDefault(s =>
            s.Type is "Intro" or "Outro" or "Recap" or "Preview" && ticks >= s.StartTicks && ticks < s.EndTicks - TicksPerSecond);

        // Settings decide per kind of segment: offer the button, skip without asking, or leave it alone.
        var action = segment == null ? "none" : Config.I.SegmentAction(segment.Type);
        if (action == "auto" && _autoSkipped.Add(segment))
        {
            App.I.Toast(segment.Type switch { "Outro" => "Skipped credits", "Recap" => "Skipped recap", "Preview" => "Skipped preview", _ => "Skipped intro" });
            SkipSegment(segment);
        }
        _activeSegment = action == "ask" ? segment : null;

        // With no segment to offer, the last seconds of an episode offer the next one.
        var remaining = duration - position;
        _upNext = _activeSegment == null && _next != null && duration > 0 && remaining > 0 && remaining <= UpNextSeconds && _scrubTo == null;

        // The scrub preview uses the same corner, so the prompt waits until the seek is settled.
        _prompt.Visible = (_activeSegment != null || _upNext) && _scrubTo == null;
        if (_activeSegment != null)
        {
            _promptButton.Text = _activeSegment.Type switch
            {
                "Outro" => _next != null ? "Next Episode" : "Skip Credits",
                "Recap" => "Skip Recap",
                "Preview" => "Skip Preview",
                _ => "Skip Intro",
            };
        }
        else if (_upNext)
        {
            var name = $"{_next.EpisodeCode}  {_next.Name}".Trim();
            _promptButton.Text = AutoPlayNext ? $"Up Next in {Math.Ceiling(remaining):0}   {name}" : $"Next Episode   {name}";
        }
    }

    private void SkipSegment(MediaSegment segment)
    {
        if (segment.Type == "Outro" && _next != null)
        {
            _position = _duration;
            _ = CloseAsync(_next);
            return;
        }
        _mpv.SeekTo(segment.EndTicks / (double)TicksPerSecond);
    }

    // ---- Input ----

    public override bool OnRawInput(InputEvent e)
    {
        if (_closing) { return true; }
        if (e.IsActionPressed("ui_cancel"))
        {
            // B backs out of a scrub first. With nothing in progress it stops playback.
            if (_scrubTo != null) { _scrubTo = null; }
            else { _ = CloseAsync(); }
            return true;
        }
        if (_mpv == null || !_loaded) { return false; }

        if (e.IsActionPressed("ui_accept"))
        {
            if (_scrubTo != null) { CommitScrub(); }
            else if (_activeSegment != null) { SkipSegment(_activeSegment); }
            else if (_upNext)
            {
                _position = _duration;
                _ = CloseAsync(_next);
            }
            else { _mpv.Paused = !_mpv.Paused; }
        }
        else if (e.IsActionPressed("ui_left", true)) { Scrub(-Config.I.SkipBackSeconds); }
        else if (e.IsActionPressed("ui_right", true)) { Scrub(Config.I.SkipForwardSeconds); }
        else if (e.IsActionPressed(Actions.Lb, true)) { Scrub(-Config.I.LongSkipSeconds); }
        else if (e.IsActionPressed(Actions.Rb, true)) { Scrub(Config.I.LongSkipSeconds); }
        else if (e.IsActionPressed(Actions.X)) { ShowTrackMenu("audio"); }
        else if (e.IsActionPressed(Actions.Y)) { ShowTrackMenu("sub"); }
        else if (e.IsActionPressed(Actions.Start)) { ShowMoreMenu(); }
        else if (!e.IsActionPressed("ui_up") && !e.IsActionPressed("ui_down")) { return false; }
        ShowOverlay();
        return true;
    }

    private void ShowTrackMenu(string type)
    {
        var tracks = _mpv.Tracks(type);
        var items = new List<(string, Action)>();
        var selected = 0;
        if (type == "sub") { items.Add(("Off", () => ChooseTrack(type, null))); }
        foreach (var track in tracks)
        {
            if (track.Selected) { selected = items.Count; }
            var id = track.Id;
            items.Add((track.Label, () => ChooseTrack(type, id)));
        }
        if (items.Count == 0)
        {
            App.I.Toast("No audio tracks.");
            return;
        }
        App.I.ShowMenu(type == "audio" ? "Audio" : "Subtitles", items, selected);
    }

    /// <summary>
    /// Switches track and tells the server which one, so its "remember my choice" setting has
    /// something to remember.
    /// </summary>
    private void ChooseTrack(string type, int? id)
    {
        if (_mpv == null) { return; }
        if (type == "audio") { _mpv.SelectAudio(id ?? 1); } else { _mpv.SelectSubtitle(id); }
        if (_source == null) { return; }

        if (type == "audio")
        {
            var audio = EmbeddedStreams("Audio");
            if (id is >= 1 && id <= audio.Count) { _audioIndex = audio[id.Value - 1].Index; }
        }
        else
        {
            var embedded = EmbeddedStreams("Subtitle");
            var external = id - embedded.Count - 1;
            _subtitleIndex = id == null ? -1
                : id <= embedded.Count ? embedded[id.Value - 1].Index
                : external < _externalSubtitles.Count ? _externalSubtitles[external.Value].Index
                : _subtitleIndex;
        }
        _ = Report(Jellyfin.I.ReportProgressAsync, type == "audio" ? "audiotrackchange" : "subtitletrackchange");
    }

    private void ShowMoreMenu()
    {
        var items = new List<(string, Action)>();
        if (_next != null) { items.Add(($"Next Episode  {_next.EpisodeCode}", () => _ = CloseAsync(_next))); }
        if (_chapters.Count > 1) { items.Add(("Chapters", ShowChapters)); }
        items.Add(("Restart", () => _mpv?.SeekTo(0)));
        if (_item != null) { items.Add(($"Quality: {QualityName(Config.I.MaxBitrateMbps)}", ShowQuality)); }
        items.Add(($"Speed: {_mpv.Speed:0.##}×", ShowSpeed));
        items.Add((_stats.Visible ? "Hide Playback Info" : "Show Playback Info", () => _stats.Visible = !_stats.Visible));
        items.Add(("Stop", () => _ = CloseAsync()));
        App.I.ShowMenu("Playback", items);
    }

    private void ShowChapters()
    {
        var current = _chapters.FindLastIndex(c => c.StartPositionTicks <= (long)(_position * TicksPerSecond));
        var items = _chapters.Select((chapter, index) =>
        {
            var start = chapter.StartPositionTicks / (double)TicksPerSecond;
            var name = string.IsNullOrWhiteSpace(chapter.Name) ? $"Chapter {index + 1}" : chapter.Name;
            return ($"{Ui.FormatTime(start)}   {name}", (Action)(() => _mpv?.SeekTo(start)));
        }).ToList();
        App.I.ShowMenu("Chapters", items, Math.Max(0, current));
    }

    private void ShowSpeed()
    {
        var speeds = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };
        var items = speeds.Select(value => ($"{value:0.##}×", (Action)(() => { if (_mpv != null) { _mpv.Speed = value; } }))).ToList();
        App.I.ShowMenu("Speed", items, Math.Max(0, Array.FindIndex(speeds, s => Math.Abs(s - _mpv.Speed) < 0.01)));
    }

    private static string QualityName(int megabits) => megabits <= 0 ? "Maximum" : $"{megabits} Mbps";

    /// <summary>Changes the bitrate ceiling and restarts the stream where it was, as the web client does.</summary>
    private void ShowQuality()
    {
        var choices = new[] { 0, 120, 80, 60, 40, 20, 15, 10, 8, 6, 4, 3, 2, 1 };
        var items = choices.Select(value => (QualityName(value), (Action)(() =>
        {
            if (value == Config.I.MaxBitrateMbps) { return; }
            Config.I.MaxBitrateMbps = value;
            _ = RestartStreamAsync();
        }))).ToList();
        App.I.ShowMenu("Quality", items, Math.Max(0, Array.IndexOf(choices, Config.I.MaxBitrateMbps)));
    }

    private async Task RestartStreamAsync()
    {
        var position = _position;
        var oldSession = _playSessionId;
        var wasTranscoding = _transcoding;
        _triedTranscode = false;
        if (await StartStreamAsync(position, false) && wasTranscoding)
        {
            _ = Jellyfin.I.StopEncodingAsync(oldSession).ContinueWith(_ => { });
        }
    }

    // ---- Reporting and shutdown ----

    private async Task Report(Func<object, Task> send, string eventName)
    {
        try
        {
            await send(new
            {
                ItemId = _item.Id,
                MediaSourceId = _mediaSourceId,
                PlaySessionId = _playSessionId,
                PositionTicks = (long)(_position * TicksPerSecond),
                IsPaused = _mpv?.Paused ?? false,
                IsMuted = false,
                CanSeek = true,
                PlayMethod = _transcoding ? "Transcode" : "DirectPlay",
                AudioStreamIndex = _audioIndex,
                SubtitleStreamIndex = _subtitleIndex,
                EventName = eventName,
            });
        }
        catch (JellyfinException) { } // A missed progress report is harmless. The next one catches up.
    }

    /// <summary>Stops playback and tells the server where the user stopped, without leaving the screen. For app exit.</summary>
    public Task StopAsync() => CloseAsync(null, false);

    public override Task OnQuitAsync() => StopAsync();

    /// <summary>Stops playback, tells the server where the user stopped, then leaves or starts the next episode.</summary>
    private async Task CloseAsync(BaseItem next = null, bool navigate = true)
    {
        if (_closing) { return; }
        _closing = true;
        _mpv?.Stop();
        if (_item != null && _mediaSourceId != null && _loaded)
        {
            var report = Report(Jellyfin.I.ReportStoppedAsync, null);
            await Task.WhenAny(report, Task.Delay(3000));
            if (_transcoding) { _ = Jellyfin.I.StopEncodingAsync(_playSessionId).ContinueWith(_ => { }); }
        }
        if (!navigate || !IsInstanceValid(this)) { return; }
        if (next != null) { App.I.Replace(new PlayerScreen(next, true)); }
        else if (App.I.Current == this)
        {
            if (_rawUrl != null) { GetTree().Quit(); } else { App.I.Pop(); }
        }
    }

    public override bool OnBack()
    {
        _ = CloseAsync();
        return true;
    }

    public override void _ExitTree()
    {
        _closing = true;
        DisplayServer.ScreenSetKeepOn(false);
        UiSounds.Muted = false;
        _mpv?.Dispose();
        _mpv = null;
    }
}
