using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>
/// Settings, laid out like RomBox's: sections on the left, the chosen section's options on the
/// right. Language and subtitle preferences are saved to the Jellyfin server, so they follow the
/// user to other clients. Everything else is saved on this device.
/// </summary>
public partial class SettingsScreen : Screen
{
    // ISO 639-2 codes, which is what Jellyfin stores for language preferences.
    private static readonly (string Label, string Code)[] Languages =
    {
        ("Any", ""), ("English", "eng"), ("Spanish", "spa"), ("French", "fre"), ("German", "ger"), ("Italian", "ita"),
        ("Portuguese", "por"), ("Dutch", "dut"), ("Swedish", "swe"), ("Norwegian", "nor"), ("Danish", "dan"),
        ("Finnish", "fin"), ("Polish", "pol"), ("Russian", "rus"), ("Turkish", "tur"), ("Arabic", "ara"),
        ("Hindi", "hin"), ("Japanese", "jpn"), ("Korean", "kor"), ("Chinese", "chi"),
    };

    private static int _lastSection;

    private readonly List<(string Name, Func<List<SettingRow>> Build)> _sections = new();
    private readonly List<Button> _sectionButtons = new();
    private readonly List<SettingRow> _rows = new();
    private VBoxContainer _list;
    private Label _help;
    private int _section = -1;

    public override string Tab => "settings";
    public override string Title => "Settings";

    public override IEnumerable<(string, string)> Hints =>
        new[] { (Actions.Accept, "Change"), (Actions.Back, "Back") };

    public override void OnEnter()
    {
        _sections.Add(("Playback", PlaybackRows));
        _sections.Add(("Video", VideoRows));
        _sections.Add(("Audio", AudioRows));
        _sections.Add(("Subtitles", SubtitleRows));
        _sections.Add(("Home", HomeRows));
        _sections.Add(("Library", LibraryRows));
        _sections.Add(("Appearance", AppearanceRows));
        _sections.Add(("Account", AccountRows));
        _sections.Add(("System", SystemRows));

        var columns = new HBoxContainer();
        Ui.Fill(columns);
        columns.AddThemeConstantOverride("separation", 0);
        AddChild(columns);

        var sections = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        sections.AddThemeConstantOverride("separation", 5);
        for (var i = 0; i < _sections.Count; i++)
        {
            var index = i;
            var button = Ui.Button(_sections[i].Name);
            button.ThemeTypeVariation = "TitleButton";
            button.Alignment = HorizontalAlignment.Left;
            button.FocusEntered += () => ShowSection(index);
            sections.AddChild(button);
            _sectionButtons.Add(button);
        }
        var sectionPanel = Ui.Pad(Ui.MicaPanel(sections, 10, 12), Ui.PanelGap, Ui.PanelGap, 0, 0);
        sectionPanel.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        columns.AddChild(sectionPanel);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(_list);
        FocusScroller.KeepVisible(scroll, _list, 70, vertical: true);
        right.AddChild(scroll);
        _help = Ui.Label("", 22, variation: "CaptionLabel");
        _help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _help.CustomMinimumSize = new Vector2(0, 60);
        right.AddChild(Ui.Pad(_help, 20, 0, 20, 0));
        var optionPanel = Ui.MicaPanel(right, 10, 12);
        optionPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        columns.AddChild(Ui.Pad(optionPanel, Ui.PanelGap, Ui.PanelGap, Ui.PanelGap, 0));

        Ui.ConfineVertical(_sectionButtons);
        foreach (var button in _sectionButtons) { button.FocusNeighborLeft = new NodePath(); }

        ShowSection(Math.Clamp(_lastSection, 0, _sections.Count - 1));
        _ = LoadPreferencesAsync();
    }

    /// <summary>The server-side preferences arrive a moment after the screen opens. Their rows appear then.</summary>
    private async Task LoadPreferencesAsync()
    {
        if (Jellyfin.I.UserPreferences != null) { return; }
        await Jellyfin.I.LoadPreferencesAsync();
        if (IsInstanceValid(this) && Jellyfin.I.UserPreferences != null) { Rebuild(); }
    }

    public override void FocusDefault() => _sectionButtons[Math.Max(0, _section)].GrabFocus();

    /// <summary>B from an option returns to the section list. B from there goes to the sidebar.</summary>
    public override bool OnBack()
    {
        if (GetViewport().GuiGetFocusOwner() is not SettingRow) { return false; }
        _sectionButtons[_section].GrabFocus();
        return true;
    }

    private void ShowSection(int index)
    {
        if (index == _section) { return; }
        _section = index;
        _lastSection = index;
        Rebuild();
    }

    /// <summary>Builds the current section's rows again, keeping focus on the same row.</summary>
    private void Rebuild()
    {
        var focused = _rows.IndexOf(GetViewport().GuiGetFocusOwner() as SettingRow);
        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }
        _rows.Clear();
        _rows.AddRange(_sections[_section].Build());
        _help.Text = "";
        MarkSection();

        var sectionButton = _sectionButtons[_section];
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            _list.AddChild(row);
            row.FocusEntered += () => _help.Text = row.Help ?? "";
        }
        // Up and Down run through the rows and stop at the ends. Left from a row without choices
        // returns to the section list.
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.FocusNeighborTop = _rows[Math.Max(0, i - 1)].GetPath();
            row.FocusNeighborBottom = _rows[Math.Min(_rows.Count - 1, i + 1)].GetPath();
            row.FocusNeighborRight = row.GetPath();
            row.FocusNeighborLeft = row.HasChoices ? row.GetPath() : sectionButton.GetPath();
        }
        foreach (var button in _sectionButtons)
        {
            button.FocusNeighborRight = _rows.Count > 0 ? _rows[0].GetPath() : button.GetPath();
        }
        if (focused >= 0 && _rows.Count > 0) { _rows[Math.Min(focused, _rows.Count - 1)].GrabFocus(); }
    }

    /// <summary>
    /// Gives the open section an accent box, so it stays marked while focus is among its options.
    /// The gliding highlight shows where focus is. The box shows which section the options belong to.
    /// </summary>
    private void MarkSection()
    {
        for (var i = 0; i < _sectionButtons.Count; i++)
        {
            if (i == _section) { _sectionButtons[i].AddThemeStyleboxOverride("normal", Ui.Box(Ui.AccentDim, 10, padX: 18, padY: 8)); }
            else { _sectionButtons[i].RemoveThemeStyleboxOverride("normal"); }
        }
    }

    // ---- Row builders ----

    private static SettingRow Pick<T>(string label, string help, IReadOnlyList<(string Label, T Value)> options, T current, Action<T> changed)
    {
        var selected = 0;
        for (var i = 0; i < options.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(options[i].Value, current)) { selected = i; }
        }
        return SettingRow.Choice(label, help, options.Select(o => o.Label).ToList(), selected, index => changed(options[index].Value));
    }

    private static SettingRow Toggle(string label, string help, bool current, Action<bool> changed) =>
        Pick(label, help, new[] { ("On", true), ("Off", false) }, current, changed);

    /// <summary>Saves one preference on the Jellyfin server.</summary>
    private static async void SavePreference(string name, JsonNode value)
    {
        try { await Jellyfin.I.SetPreferenceAsync(name, value); }
        catch (JellyfinException e) { App.I.Toast(e.Message); }
    }

    private static SettingRow LanguageRow(string label, string help, string preference)
    {
        var current = Jellyfin.I.Preference(preference);
        var options = Languages.ToList();
        // A language set from another client that is not in the list still shows, by its code.
        if (options.All(o => o.Code != current)) { options.Add((current, current)); }
        return Pick(label, help, options, current, code => SavePreference(preference, JsonValue.Create(code)));
    }

    private static SettingRow ServerNote() =>
        SettingRow.Button("Language preferences", "These are stored on the Jellyfin server and could not be loaded.", "Unavailable", null);

    private List<SettingRow> PlaybackRows()
    {
        var config = Config.I;
        var segmentChoices = new[] { ("Ask to skip", "ask"), ("Skip automatically", "auto"), ("Do nothing", "none") };
        var rows = new List<SettingRow>
        {
            Pick("Maximum streaming quality", "Above this bitrate the server converts the video to a smaller stream. Lower it on a slow connection.",
                new[] { ("Maximum", 0), ("120 Mbps", 120), ("80 Mbps", 80), ("60 Mbps", 60), ("40 Mbps", 40), ("20 Mbps", 20), ("15 Mbps", 15),
                    ("10 Mbps", 10), ("8 Mbps", 8), ("6 Mbps", 6), ("4 Mbps", 4), ("3 Mbps", 3), ("2 Mbps", 2), ("1 Mbps", 1) },
                config.MaxBitrateMbps, v => config.MaxBitrateMbps = v),
            Pick("Hardware decoding", "Decodes video on the graphics card. Turn it off if video shows glitches or will not start.",
                new[] { ("Automatic", "auto-safe"), ("Automatic, copy to memory", "auto-copy-safe"), ("Off", "no") },
                config.HardwareDecoding, v => config.HardwareDecoding = v),
            Pick("Buffer size", "How much video is downloaded ahead and held in memory. Larger rides out network drops and uses that much more RAM.",
                new[] { ("50 MB", 50), ("100 MB", 100), ("200 MB", 200), ("500 MB", 500), ("1 GB", 1024), ("2 GB", 2048) },
                config.BufferMegabytes, v => config.BufferMegabytes = v),
            Pick("Skip forward", "How far Right jumps during playback.",
                new[] { ("5 seconds", 5), ("10 seconds", 10), ("15 seconds", 15), ("30 seconds", 30), ("60 seconds", 60) },
                config.SkipForwardSeconds, v => config.SkipForwardSeconds = v),
            Pick("Skip back", "How far Left jumps during playback.",
                new[] { ("5 seconds", 5), ("10 seconds", 10), ("15 seconds", 15), ("30 seconds", 30) },
                config.SkipBackSeconds, v => config.SkipBackSeconds = v),
            Pick("Long skip", "How far LB and RB jump during playback.",
                new[] { ("1 minute", 60), ("2 minutes", 120), ("5 minutes", 300), ("10 minutes", 600) },
                config.LongSkipSeconds, v => config.LongSkipSeconds = v),
            Pick("Rewind when resuming", "Steps back a little from where you stopped, to pick up the thread.",
                new[] { ("Off", 0), ("5 seconds", 5), ("10 seconds", 10), ("30 seconds", 30) },
                config.ResumeRewindSeconds, v => config.ResumeRewindSeconds = v),
            Pick("Intros", "What happens when an episode's intro starts. Needs intro markers on the server.", segmentChoices, config.IntroAction, v => config.IntroAction = v),
            Pick("Credits", "What happens when the credits start. With a next episode waiting, skipping goes to it.", segmentChoices, config.CreditsAction, v => config.CreditsAction = v),
            Pick("Recaps and previews", "What happens at a recap or a preview of the next episode.", segmentChoices, config.OtherSegmentAction, v => config.OtherSegmentAction = v),
        };
        rows.Add(Toggle("Playback info", "Shows codec, resolution, decoder and dropped frames over the video. It can also be switched from the More menu while playing.",
            config.PlaybackStats, v => config.PlaybackStats = v));
        if (Jellyfin.I.UserPreferences != null)
        {
            rows.Insert(2, Toggle("Play next episode automatically", "Starts the next episode when one ends. Saved to your Jellyfin account.",
                Jellyfin.I.Preference("EnableNextEpisodeAutoPlay", true), v => SavePreference("EnableNextEpisodeAutoPlay", JsonValue.Create(v))));
        }
        return rows;
    }

    private List<SettingRow> AudioRows()
    {
        var config = Config.I;
        var rows = new List<SettingRow>();
        if (Jellyfin.I.UserPreferences != null)
        {
            rows.Add(LanguageRow("Preferred audio language", "The audio track picked when a video has several. Saved to your Jellyfin account.", "AudioLanguagePreference"));
            rows.Add(Toggle("Play default audio track", "Uses the track the file marks as default, whatever its language. Saved to your Jellyfin account.",
                Jellyfin.I.Preference("PlayDefaultAudioTrack", true), v => SavePreference("PlayDefaultAudioTrack", JsonValue.Create(v))));
        }
        else { rows.Add(ServerNote()); }
        if (Jellyfin.I.UserPreferences != null)
        {
            rows.Add(Toggle("Remember audio choice", "Keeps the audio track you pick for a show's other episodes. Saved to your Jellyfin account.",
                Jellyfin.I.Preference("RememberAudioSelections", true), v => SavePreference("RememberAudioSelections", JsonValue.Create(v))));
        }
        rows.Add(Pick("Speakers", "Stereo mixes surround sound down to two channels, for TVs and headphones.",
            new[] { ("Automatic", "auto-safe"), ("Stereo", "stereo") }, config.AudioChannels, v => config.AudioChannels = v));
        rows.Add(Toggle("Night mode", "Evens out loud and quiet passages, so dialogue stays audible at low volume.",
            config.AudioNightMode, v => config.AudioNightMode = v));
        rows.Add(Pick("Audio delay", "Shifts the sound against the picture, for a TV or soundbar that is out of sync. Positive plays the sound later.",
            DelayOptions(), config.AudioDelayMs, v => config.AudioDelayMs = v));
        rows.Add(Toggle("Audio passthrough", "Sends Dolby and DTS audio to a receiver or soundbar undecoded. Leave off without one.",
            config.AudioPassthrough, v => config.AudioPassthrough = v));
        return rows;
    }

    private List<SettingRow> SubtitleRows()
    {
        var config = Config.I;
        var rows = new List<SettingRow>();
        if (Jellyfin.I.UserPreferences != null)
        {
            rows.Add(LanguageRow("Preferred subtitle language", "The subtitle language to look for. Saved to your Jellyfin account.", "SubtitleLanguagePreference"));
            rows.Add(Pick("Subtitle mode", "Default follows each file's flags. Smart shows subtitles when the audio is in another language. Saved to your Jellyfin account.",
                new[] { ("Default", "Default"), ("Smart", "Smart"), ("Only forced", "OnlyForced"), ("Always", "Always"), ("Off", "None") },
                Jellyfin.I.Preference("SubtitleMode", "Default"), v => SavePreference("SubtitleMode", JsonValue.Create(v))));
        }
        else { rows.Add(ServerNote()); }
        if (Jellyfin.I.UserPreferences != null)
        {
            rows.Add(Toggle("Remember subtitle choice", "Keeps the subtitle track you pick for a show's other episodes. Saved to your Jellyfin account.",
                Jellyfin.I.Preference("RememberSubtitleSelections", true), v => SavePreference("RememberSubtitleSelections", JsonValue.Create(v))));
        }
        rows.Add(Pick("Size", "Size of text subtitles. Styled subtitles keep their own size.",
            new[] { ("Small", "0.8"), ("Normal", "1"), ("Large", "1.3"), ("Extra large", "1.6") }, config.SubtitleScale, v => config.SubtitleScale = v));
        rows.Add(Pick("Position", "How high subtitles sit above the bottom of the picture.",
            new[] { ("Bottom", 100), ("Raised", 92), ("Higher", 84) }, config.SubtitlePosition, v => config.SubtitlePosition = v));
        rows.Add(Pick("Colour", "Colour of text subtitles.",
            new[] { ("White", "#FFFFFF"), ("Yellow", "#FFE600"), ("Light grey", "#C8C8C8"), ("Cyan", "#7FE9FF"), ("Green", "#8CFF8C") },
            config.SubtitleColor, v => config.SubtitleColor = v));
        rows.Add(Toggle("Background box", "Draws a dark box behind subtitle text, which is easier to read over bright scenes.",
            config.SubtitleBox, v => config.SubtitleBox = v));
        rows.Add(Toggle("Restyle styled subtitles", "Applies your size, colour and position to subtitles that carry their own styling, such as anime releases.",
            config.SubtitleOverrideStyles, v => config.SubtitleOverrideStyles = v));
        rows.Add(Pick("Subtitle delay", "Shifts subtitles against the picture. Positive shows them later.",
            DelayOptions(), config.SubtitleDelayMs, v => config.SubtitleDelayMs = v));
        return rows;
    }

    private static List<(string, int)> DelayOptions() =>
        Enumerable.Range(-10, 21).Select(step => step * 50).Select(ms => (ms == 0 ? "None" : $"{ms:+0;-0} ms", ms)).ToList();

    private List<SettingRow> VideoRows()
    {
        var config = Config.I;
        return new List<SettingRow>
        {
            Pick("Picture shape", "Fit shows the whole picture. Fill zooms in to remove black bars and crops the edges. Stretch distorts it to fill the screen.",
                new[] { ("Fit", "fit"), ("Fill", "fill"), ("Stretch", "stretch") }, config.VideoZoom, v => config.VideoZoom = v),
            Pick("Scaling quality", "Sharper costs more graphics power. Faster suits handhelds and saves battery.",
                new[] { ("Balanced", "default"), ("Sharper", "high-quality"), ("Faster", "fast") }, config.VideoScaling, v => config.VideoScaling = v),
            Pick("HDR tone mapping", "How HDR video is fitted to this screen's brightness range.",
                new[] { ("Automatic", "auto"), ("BT.2390", "bt.2390"), ("Hable", "hable"), ("Mobius", "mobius"), ("Reinhard", "reinhard"), ("Clip", "clip") },
                config.ToneMapping, v => config.ToneMapping = v),
            Toggle("Deinterlace", "Smooths the combing lines in interlaced video, such as old TV recordings and DVDs.", config.Deinterlace, v => config.Deinterlace = v),
            Toggle("Reduce banding", "Smooths visible steps in gradients such as skies. Costs a little graphics power.", config.Deband, v => config.Deband = v),
        };
    }

    private List<SettingRow> HomeRows()
    {
        var config = Config.I;
        var rows = new List<SettingRow>
        {
            Toggle("Continue Watching", "Shows what you have started on the Home page.", config.HomeContinueWatching, v => config.HomeContinueWatching = v),
            Toggle("Next Up", "Shows the next episode of shows you are watching.", config.HomeNextUp, v => config.HomeNextUp = v),
            Pick("Next Up remembers shows for", "Drops a show from Next Up once this long has passed since you last watched it.",
                new[] { ("Always", 0), ("1 month", 30), ("3 months", 90), ("6 months", 180), ("1 year", 365) }, config.NextUpDays, v => config.NextUpDays = v),
            Toggle("Rewatching in Next Up", "Includes shows you have finished and started again.", config.NextUpRewatching, v => config.NextUpRewatching = v),
            Toggle("Libraries", "Shows a row of your libraries on the Home page.", config.HomeLibraries, v => config.HomeLibraries = v),
            Toggle("Latest additions", "Shows a row of new items for each library.", config.HomeLatest, v => config.HomeLatest = v),
        };
        if (Jellyfin.I.UserPreferences != null)
        {
            rows.Add(Toggle("Hide watched in Latest", "Leaves items you have already watched out of the Latest rows. Saved to your Jellyfin account.",
                Jellyfin.I.Preference("HidePlayedInLatest", true), v => SavePreference("HidePlayedInLatest", JsonValue.Create(v))));
        }
        return rows;
    }

    private List<SettingRow> LibraryRows()
    {
        var config = Config.I;
        var sorts = LibraryScreen.SortNames.Select((name, index) => (name, index)).ToList();
        return new List<SettingRow>
        {
            Pick("Default sort", "The order TV, Movies and other libraries open in. X changes it while browsing.", sorts, config.LibrarySort, v => config.LibrarySort = v),
            Pick("Card size", "Size of posters and thumbnails on every page.",
                new[] { ("Small", 0.85f), ("Medium", 1f), ("Large", 1.15f) }, config.CardScale, v => config.CardScale = v),
            Toggle("Titles under cards", "Shows the title and year under each poster. Off fits more artwork on screen.", config.CardTitles, v => config.CardTitles = v),
            Toggle("Backdrops", "Shows a title's background artwork behind its details page.", config.Backdrops, v => config.Backdrops = v),
        };
    }

    private List<SettingRow> AppearanceRows()
    {
        var config = Config.I;
        void Restyle()
        {
            Appearance.ApplySaved();
            App.I.RefreshChrome();
            MarkSection();
        }
        return new List<SettingRow>
        {
            Pick("Style", "Glass has animated backgrounds and frosted panels. Simple uses flat colours and is easier on handhelds.",
                Appearance.Styles.Select(s => (s, s)).ToList(), config.AppStyle, v => { config.AppStyle = v; Restyle(); }),
            Pick("Theme", "The colour palette.", Palettes.Names.Select(n => (n, n)).ToList(), config.AppTheme, v => { config.AppTheme = v; Restyle(); }),
            Pick("Background", "The animated background under the Glass style.",
                Palettes.Backgrounds.Select(b => (b, b)).ToList(), config.AppBackground, v => { config.AppBackground = v; Restyle(); }),
            Pick("Interface size", "Makes everything larger or smaller. Auto picks a larger size on handheld screens.",
                new[] { "Auto", "85%", "100%", "115%", "130%", "150%" }.Select(s => (s, s)).ToList(), config.InterfaceSize, v =>
                {
                    config.InterfaceSize = v;
                    InterfaceScale.Apply(GetTree().Root, v);
                }),
            Pick("Clock", "The clock in the top bar.",
                new[] { ("System format", "system"), ("12-hour", "12"), ("24-hour", "24"), ("Hidden", "off") }, config.Clock, v =>
                {
                    config.Clock = v;
                    App.I.RefreshClock();
                }),
            Toggle("Interface sounds", "Plays a sound when you move, select and go back.", config.UiSounds, v => config.UiSounds = v),
            Toggle("Fullscreen", "Fills the screen. F11 switches too.", App.IsFullscreen, v =>
            {
                if (v != App.IsFullscreen) { App.I.ToggleFullscreen(); }
            }),
        };
    }

    private List<SettingRow> AccountRows()
    {
        var config = Config.I;
        var seerr = Seerr.I;
        return new List<SettingRow>
        {
            SettingRow.Button("Server", config.Server, config.ServerName, null),
            SettingRow.Button("Signed in as", "Sign out to change user or server.", config.UserName, () => _ = SignOutAsync()),
            SettingRow.Button("Quick Connect", "Signs in another device as you. Enter the code that device is showing.", "Enter a code", () => _ = QuickConnectAsync()),
            SettingRow.Button("Seerr", seerr.Connected ? "Choose to disconnect from Seerr." : "Choose to connect to a Seerr server for requests.",
                seerr.Connected ? $"Connected as {seerr.User.Name}" : "Not connected", () => _ = SeerrAsync()),
        };
    }

    private List<SettingRow> SystemRows()
    {
        var megabytes = ImageCache.I.DiskBytes() / 1_048_576.0;
        return new List<SettingRow>
        {
            SettingRow.Button("Artwork cache", "Posters and backdrops saved on this device. Clearing frees the space, and artwork downloads again as you browse.",
                $"{megabytes:0} MB", () => _ = ClearCacheAsync()),
            SettingRow.Button("Padfin", "Choose to check for a newer version.", $"Version {Jellyfin.ClientVersion}", () => _ = UpdateFlow.CheckAndOfferAsync(true)),
            Toggle("Test builds", "Offers early builds as updates, before they become full releases. They get new features first and less testing.",
                Config.I.PreReleaseBuilds, v => Config.I.PreReleaseBuilds = v),
            SettingRow.Button("Exit Padfin", "", "", () => _ = App.I.QuitAsync()),
        }.Concat(SteamRows()).ToList();
    }

    /// <summary>The Steam library row, on desktop builds where adding a shortcut is possible.</summary>
    private static IEnumerable<SettingRow> SteamRows()
    {
        if (!SteamShortcuts.IsSupportedPlatform) { yield break; }
        var launched = SteamShortcuts.IsLaunchedThroughSteam;
        yield return SettingRow.Button("Steam library",
            launched ? "Padfin is running from your Steam library." : $"{SteamOffer.DescribeAction()} to open Padfin from Steam, with Steam Input and the overlay.",
            SteamOffer.DescribeStatus(), launched ? null : () => _ = SteamOffer.StartThroughSteamAsync());
    }

    private async Task ClearCacheAsync()
    {
        if (await App.I.ChooseAsync("Clear saved artwork?", new[] { "Clear", "Cancel" }) != 0) { return; }
        ImageCache.I.Clear();
        if (IsInstanceValid(this)) { Rebuild(); }
    }

    private static async Task QuickConnectAsync()
    {
        var code = await App.I.PromptAsync("Quick Connect code from the other device");
        if (string.IsNullOrWhiteSpace(code)) { return; }
        try
        {
            var approved = await Jellyfin.I.AuthorizeQuickConnectAsync(code.Trim());
            App.I.Toast(approved ? "The other device is signed in." : "That code was not accepted.");
        }
        catch (JellyfinException e) { App.I.Toast(e.Status == System.Net.HttpStatusCode.NotFound ? "That code was not found." : e.Message); }
    }

    private static async Task SignOutAsync()
    {
        if (await App.I.ChooseAsync($"Sign out {Config.I.UserName}?", new[] { "Sign Out", "Cancel" }) != 0) { return; }
        Jellyfin.I.SignOut();
        App.I.ResetTo(new ConnectScreen());
    }

    private async Task SeerrAsync()
    {
        if (Seerr.I.Connected)
        {
            if (await App.I.ChooseAsync("Disconnect from Seerr?", new[] { "Disconnect", "Cancel" }) != 0) { return; }
            Seerr.I.Disconnect();
        }
        else if (!await SeerrSetup.RunAsync()) { return; }
        if (IsInstanceValid(this)) { Rebuild(); }
    }
}
