using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>
/// Root node. The screen stack, overlays, input routing, footer hints and toasts come from FishUI's
/// AppShell. This adds what is Padfin's own: its pages, saved settings, the server in the top bar,
/// and startup.
/// </summary>
public partial class App : AppShell
{
    public static new App I => (App)AppShell.I;

    private TextureRect _serverIcon;
    private Label _serverInitial;
    private Label _userName;
    private string _iconServer;

    /// <summary>The pages in the navigation sidebar, in order.</summary>
    protected override IReadOnlyList<ShellTab> Tabs { get; } = new[]
    {
        new ShellTab("home", "Home", () => new HomeScreen()),
        new ShellTab("tv", "TV", () => new LibraryScreen("tv", "TV", "Series")),
        new ShellTab("movies", "Movies", () => new LibraryScreen("movies", "Movies", "Movie")),
        new ShellTab("collections", "Collections", () => new LibraryScreen("collections", "Collections", "BoxSet")),
        new ShellTab("requests", "Requests", () => new RequestsScreen()),
        new ShellTab("settings", "Settings", () => new SettingsScreen()),
    };

    // ---- Saved settings ----

    protected override void ApplyAppearance()
    {
        var config = Config.I;
        Appearance.Apply(config.AppStyle, config.AppBackground, config.AppTheme);
    }

    /// <summary>Applies the style, palette and background saved in settings.</summary>
    public void ApplySavedAppearance() => ApplyAppearance();

    protected override string InterfaceSize => Config.I.InterfaceSize;

    protected override bool StartFullscreen => Config.I.Fullscreen;

    protected override void OnFullscreenChanged(bool fullscreen) => Config.I.Fullscreen = fullscreen;

    // ---- Startup ----

    protected override async Task StartAsync(string[] args)
    {
        UiSounds.Enabled = Config.I.UiSounds;
        ClockFormat = Config.I.Clock;
        SteamShortcuts.Configure("Padfin", "padfin");
        Jellyfin.I.SessionExpired += () => Callable.From(HandleSessionExpired).CallDeferred();

        // "-- --add-to-steam" writes the Steam shortcut and exits, for scripts and installers.
        if (SteamShortcuts.RunCommandLineAddIfRequested())
        {
            GetTree().Quit();
            return;
        }

        // Developer aid: "-- --play <file or url>" opens the player directly.
        var play = Array.IndexOf(args, "--play");
        if (play >= 0 && play + 1 < args.Length)
        {
            ResetTo(new PlayerScreen(args[play + 1]));
            return;
        }

        if (!Config.I.HasSession)
        {
            ResetTo(new ConnectScreen());
            _ = StartupOffersAsync();
            return;
        }
        try
        {
            if (await Jellyfin.I.ValidateSessionAsync())
            {
                _ = Seerr.I.RestoreAsync();
                _ = Jellyfin.I.LoadPreferencesAsync();
                ResetTo(new HomeScreen());
                _ = StartupOffersAsync();
            }
            else { ResetTo(new ConnectScreen()); }
        }
        catch (JellyfinException e)
        {
            if (Current == null) { ResetTo(new ConnectScreen()); }
            Toast(e.Message);
        }
    }

    /// <summary>The questions asked at startup, one at a time: an update first, then adding Padfin to Steam.</summary>
    private static async Task StartupOffersAsync()
    {
        if (await UpdateFlow.CheckAndOfferAsync(false)) { return; }
        await SteamOffer.OfferIfUsefulAsync(Config.I.OfferSteamLaunch, () => Config.I.OfferSteamLaunch = false);
    }

    private void HandleSessionExpired()
    {
        Jellyfin.I.SignOut();
        if (Current is not ConnectScreen and not LoginScreen) { ResetTo(new ConnectScreen()); }
    }

    // ---- Navigation ----

    /// <summary>Opens the right screen for an item: a library, a details page, or the player.</summary>
    public void Open(BaseItem item, bool play = false)
    {
        if (item == null) { return; }
        if (item.IsContainer) { Push(new LibraryScreen(item)); }
        else if (play && item.IsPlayable) { Push(new PlayerScreen(item, false)); }
        else if (item.Type is "Movie" or "Series" or "Season" or "Episode" or "Video" or "MusicVideo") { Push(new DetailsScreen(item)); }
        else { Toast($"{item.Type} items are not supported yet."); }
    }

    // ---- Top bar ----

    protected override string TopBarTitle => Config.I.ServerName == "" ? "Jellyfin" : Config.I.ServerName;

    /// <summary>The top bar names the server, so it waits until one is chosen.</summary>
    protected override bool TopBarAvailable => Config.I.Server != "";

    /// <summary>The server's own icon, over a badge with its initial for servers that offer none.</summary>
    protected override void BuildTopBarStart(HBoxContainer row)
    {
        var badge = new PanelContainer { CustomMinimumSize = new Vector2(52, 52), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        badge.AddThemeStyleboxOverride("panel", Ui.Box(Ui.AccentDim, 12));
        _serverInitial = Ui.Label("", 30);
        _serverInitial.HorizontalAlignment = HorizontalAlignment.Center;
        _serverInitial.VerticalAlignment = VerticalAlignment.Center;
        badge.AddChild(_serverInitial);
        _serverIcon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        badge.AddChild(_serverIcon);
        row.AddChild(badge);
    }

    protected override void BuildTopBarEnd(HBoxContainer row)
    {
        _userName = Ui.Label("", variation: "CaptionLabel");
        row.AddChild(_userName);
    }

    protected override void OnChromeRefreshed(Screen screen)
    {
        if (!TopBarAvailable) { return; }
        _serverInitial.Text = TopBarTitle[..1].ToUpperInvariant();
        _userName.Text = Config.I.UserName;
        if (_iconServer != Config.I.Server) { _ = LoadServerIconAsync(Config.I.Server); }
    }

    private async Task LoadServerIconAsync(string server)
    {
        _iconServer = server;
        _serverIcon.Texture = null;
        var texture = await ImageCache.I.GetAsync(await Jellyfin.I.ServerIconUrlAsync());
        if (_iconServer != server) { return; }
        _serverIcon.Texture = texture;
        _serverInitial.Visible = texture == null;
    }
}
