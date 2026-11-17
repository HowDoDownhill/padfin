using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>Root node: the screen stack, modal overlays, input routing, footer hints, and toasts.</summary>
public partial class App : Control
{
    public static App I { get; private set; }

    private readonly List<Screen> _stack = new();
    private readonly List<Overlay> _overlays = new();
    private Control _screenHost;
    private Control _overlayHost;
    private HBoxContainer _hints;
    private Control _footer;
    private Control _sidebar;
    private Control _topBar;
    private TextureRect _serverIcon;
    private Label _serverInitial;
    private Label _serverName;
    private Label _pageTitle;
    private Label _pageInfo;
    private Label _userName;
    private Label _clock;
    private double _clockIn;
    private string _iconServer;
    private readonly List<Button> _tabButtons = new();

    /// <summary>The pages in the navigation sidebar, in order.</summary>
    private static readonly (string Id, string Label, Func<Screen> Create)[] Tabs =
    {
        ("home", "Home", () => new HomeScreen()),
        ("tv", "TV", () => new LibraryScreen("tv", "TV", "Series")),
        ("movies", "Movies", () => new LibraryScreen("movies", "Movies", "Movie")),
        ("collections", "Collections", () => new LibraryScreen("collections", "Collections", "BoxSet")),
        ("requests", "Requests", () => new RequestsScreen()),
        ("settings", "Settings", () => new SettingsScreen()),
    };
    private PanelContainer _toast;
    private Label _toastLabel;
    private Tween _toastTween;

    // Held D-pad or stick repeats, since Godot only repeats keyboard keys.
    private const double ShoulderHoldSeconds = 0.4;
    private string _shoulder;
    private double _shoulderFor;

    private string _heldAction;
    private double _heldFor;
    private double _repeatIn;

    public Screen Current => _stack.Count > 0 ? _stack[^1] : null;
    private Overlay TopOverlay => _overlays.Count > 0 ? _overlays[^1] : null;

    public override void _EnterTree()
    {
        I = this;
        Actions.Register();
    }

    public override void _Ready()
    {
        // RomBox's look: palette, Glass or Simple style, gliding selection highlight, interface sounds.
        Appearance.ApplySaved();
        InterfaceScale.Apply(GetTree().Root, Config.I.InterfaceSize);
        SelectionHighlighter.Attach(GetViewport());
        AddChild(new UiSounds());

        // Closing the window goes through QuitAsync, so a playing video can report where it stopped.
        GetTree().AutoAcceptQuit = false;

        var args = OS.GetCmdlineUserArgs();
        if (Config.I.Fullscreen && !args.Contains("--windowed")) { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen); }
        else { DisplayServer.WindowSetSize(new Vector2I(1600, 900)); }

        var background = new ColorRect
        {
            Material = GD.Load<ShaderMaterial>("res://assets/materials/moving_background.tres"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Ui.Fill(background);
        AddChild(background);

        var layout = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        layout.AddThemeConstantOverride("separation", 0);
        Ui.Fill(layout);
        AddChild(layout);

        BuildTopBar(layout);

        var body = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        layout.AddChild(body);
        BuildSidebar(body);

        _screenHost = new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(_screenHost);

        BuildFooter(layout);

        _overlayHost = new Control { MouseFilter = MouseFilterEnum.Ignore };
        Ui.Fill(_overlayHost);
        AddChild(_overlayHost);

        BuildToast();

        Jellyfin.I.SessionExpired += () => Callable.From(HandleSessionExpired).CallDeferred();
        _ = StartAsync(args);
    }

    private async Task StartAsync(string[] args)
    {
        // Developer aid: "-- --shot <png> [seconds]" saves a screenshot and exits.
        var shot = Array.IndexOf(args, "--shot");
        if (shot >= 0 && shot + 1 < args.Length)
        {
            var wait = shot + 2 < args.Length && double.TryParse(args[shot + 2], out var seconds) ? seconds : 4;
            GetTree().CreateTimer(wait).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng(args[shot + 1]);
                // A video is stopped the normal way first, so the server gets its stop report.
                if (Current is PlayerScreen player)
                {
                    player.OnBack();
                    GetTree().CreateTimer(3).Timeout += () => GetTree().Quit();
                }
                else { GetTree().Quit(); }
            };
        }

        // Developer aid: "-- --keys ui_down,ui_accept,pf_y" presses actions in order, for scripted smoke tests.
        var keys = Array.IndexOf(args, "--keys");
        if (keys >= 0 && keys + 1 < args.Length) { _ = PressKeysAsync(args[keys + 1].Split(',')); }

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
            if (_stack.Count == 0) { ResetTo(new ConnectScreen()); }
            Toast(e.Message);
        }
    }

    /// <summary>The questions asked at startup, one at a time: an update first, then adding Padfin to Steam.</summary>
    private static async Task StartupOffersAsync()
    {
        if (await UpdateFlow.CheckAndOfferAsync(false)) { return; }
        await SteamOffer.OfferIfUsefulAsync();
    }

    private async Task PressKeysAsync(string[] actions)
    {
        await Task.Delay(2500);
        foreach (var action in actions)
        {
            // "joy:0" presses a controller button by index, so prompts switch to controller glyphs.
            if (action == "quit")
            {
                // Exits the way closing the window does.
                await QuitAsync();
                return;
            }
            if (action.StartsWith("hold:"))
            {
                // "hold:pf_rb" keeps an action down long enough to count as a hold.
                Input.ParseInputEvent(new InputEventAction { Action = action[5..], Pressed = true });
                await Task.Delay(700);
                Input.ParseInputEvent(new InputEventAction { Action = action[5..], Pressed = false });
            }
            else if (action.StartsWith("joy:"))
            {
                var button = (JoyButton)int.Parse(action[4..]);
                Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = true });
                Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = false });
            }
            else
            {
                Input.ParseInputEvent(new InputEventAction { Action = action.Trim(), Pressed = true });
                Input.ParseInputEvent(new InputEventAction { Action = action.Trim(), Pressed = false });
            }
            await Task.Delay(700);
        }
    }

    private void HandleSessionExpired()
    {
        Jellyfin.I.SignOut();
        if (Current is not ConnectScreen and not LoginScreen) { ResetTo(new ConnectScreen()); }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) { _ = QuitAsync(); }
    }

    /// <summary>Exits the app. A playing video is stopped first, so the server saves its position.</summary>
    public async Task QuitAsync()
    {
        if (Current is PlayerScreen player) { await player.StopAsync(); }
        GetTree().Quit();
    }

    // ---- Navigation ----

    public void Push(Screen screen)
    {
        var previous = Current;
        if (previous != null)
        {
            previous.SaveFocus();
            previous.Visible = false;
        }
        _stack.Add(screen);
        Show(screen);
    }

    public void Pop()
    {
        if (_stack.Count <= 1) { return; }
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        top.QueueFree();
        var screen = Current;
        screen.Visible = true;
        RefreshChrome();
        screen.RestoreFocus();
        screen.OnResume();
    }

    /// <summary>Swaps the top screen for another, for moving to the next episode.</summary>
    public void Replace(Screen screen)
    {
        if (_stack.Count > 0)
        {
            _stack[^1].QueueFree();
            _stack.RemoveAt(_stack.Count - 1);
        }
        _stack.Add(screen);
        Show(screen);
    }

    public void ResetTo(Screen screen)
    {
        foreach (var overlay in _overlays.ToArray()) { CloseOverlay(overlay); }
        foreach (var old in _stack) { old.QueueFree(); }
        _stack.Clear();
        _stack.Add(screen);
        Show(screen);
    }

    private void Show(Screen screen)
    {
        _screenHost.AddChild(screen);
        screen.Modulate = new Color(1, 1, 1, 0);
        screen.CreateTween().TweenProperty(screen, "modulate:a", 1f, 0.15);
        RefreshChrome();
        screen.OnEnter();
        screen.FocusDefault();
    }

    public void Back()
    {
        var screen = Current;
        if (screen == null || screen.OnBack()) { return; }
        if (_stack.Count > 1) { Pop(); }
        else if (screen.Tab != null) { FocusSidebar(); }
    }

    /// <summary>Opens the right screen for an item: a library, a details page, or the player.</summary>
    public void Open(BaseItem item, bool play = false)
    {
        if (item == null) { return; }
        if (item.IsContainer) { Push(new LibraryScreen(item)); }
        else if (play && item.IsPlayable) { Push(new PlayerScreen(item, false)); }
        else if (item.Type is "Movie" or "Series" or "Season" or "Episode" or "Video" or "MusicVideo") { Push(new DetailsScreen(item)); }
        else { Toast($"{item.Type} items are not supported yet."); }
    }

    // ---- Overlays ----

    public void OpenOverlay(Overlay overlay)
    {
        overlay.PreviousFocus = GetViewport().GuiGetFocusOwner();
        _overlays.Add(overlay);
        _overlayHost.AddChild(overlay);
        overlay.Open();
        overlay.FocusDefault();
    }

    public void CloseOverlay(Overlay overlay)
    {
        if (!_overlays.Remove(overlay)) { return; }
        overlay.Closed += overlay.QueueFree;
        overlay.Close();
        var focus = overlay.PreviousFocus;
        if (focus != null && IsInstanceValid(focus) && focus.IsInsideTree() && focus.IsVisibleInTree()) { focus.GrabFocus(); }
        else if (TopOverlay != null) { TopOverlay.FocusDefault(); }
        else { GetViewport().GuiReleaseFocus(); Current?.RestoreFocus(); }
        // Last, because code waiting on the overlay's answer may open the next overlay right away,
        // and that one must take focus after this one has given it back.
        overlay.OnDismissed();
    }

    /// <summary>Opens the jump grid, RomBox's way of picking one of many.</summary>
    public void ShowJump(string title, IReadOnlyList<(string Label, string Caption)> tiles, int selected, Action<int> chosen) =>
        OpenOverlay(new JumpOverlay(title, tiles, selected, chosen));

    public void ShowMenu(string title, IReadOnlyList<(string Label, Action Action)> items, int selected = 0) =>
        OpenOverlay(new MenuOverlay(title, items, selected));

    /// <summary>Shows a menu and completes with the index chosen, or -1 when it is dismissed.</summary>
    /// <param name="body">Optional text explaining the question, shown above the choices.</param>
    public Task<int> ChooseAsync(string title, IReadOnlyList<string> options, string body = null)
    {
        var result = new TaskCompletionSource<int>();
        var items = options.Select((label, index) => (label, (Action)(() => result.TrySetResult(index)))).ToList();
        OpenOverlay(new MenuOverlay(title, items, 0, () => result.TrySetResult(-1), body));
        return result.Task;
    }

    /// <summary>Asks for text with the on-screen keyboard. Null when cancelled.</summary>
    public Task<string> PromptAsync(string title, string initial = "", bool secret = false)
    {
        var prompt = new TextPrompt(title, initial, secret);
        OpenOverlay(prompt);
        return prompt.Result;
    }

    // ---- Chrome ----

    /// <summary>The bar across the top: the server's icon and name, the page, and the clock.</summary>
    private void BuildTopBar(Control layout)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);

        // The server's own icon, over a badge with its initial for servers that offer none.
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

        _serverName = Ui.Label("", variation: "DisplayLabel");
        row.AddChild(_serverName);
        _pageTitle = Ui.Label("", variation: "TitleLabel");
        _pageTitle.Modulate = new Color(1, 1, 1, 0.65f);
        row.AddChild(_pageTitle);
        row.AddChild(Ui.Spacer(expand: true));
        _pageInfo = Ui.Label("", variation: "CaptionLabel");
        row.AddChild(_pageInfo);
        _userName = Ui.Label("", variation: "CaptionLabel");
        row.AddChild(_userName);
        _clock = Ui.Label("", variation: "TitleLabel");
        row.AddChild(_clock);

        _topBar = Ui.Pad(Ui.MicaPanel(row, 20, 8), Ui.PanelGap, Ui.PanelGap, Ui.PanelGap, 0);
        _topBar.Visible = false;
        layout.AddChild(_topBar);
    }

    private void RefreshTopBar(Screen screen)
    {
        _topBar.Visible = screen is { ShowChrome: true, ShowTopBar: true } && Config.I.Server != "";
        if (!_topBar.Visible) { return; }
        var name = Config.I.ServerName == "" ? "Jellyfin" : Config.I.ServerName;
        _serverName.Text = name;
        _serverInitial.Text = name[..1].ToUpperInvariant();
        // A sidebar page is already marked in the sidebar, so only other screens are named here.
        _pageTitle.Text = screen.Tab == null ? screen.Title ?? "" : "";
        _pageInfo.Text = screen.Info ?? "";
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

    /// <summary>Shows the time in the format chosen in settings, or hides the clock.</summary>
    public void RefreshClock()
    {
        var format = Config.I.Clock switch { "12" => "h:mm tt", "24" => "HH:mm", _ => "t" };
        _clock.Visible = Config.I.Clock != "off";
        _clock.Text = DateTime.Now.ToString(format);
    }

    /// <summary>Updates the top bar's status text when the current screen changes it.</summary>
    public void RefreshInfo(Screen screen)
    {
        if (screen == Current && _pageInfo != null) { _pageInfo.Text = screen.Info ?? ""; }
    }

    private void BuildSidebar(Control body)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        column.AddThemeConstantOverride("separation", 5);
        foreach (var (id, label, _) in Tabs)
        {
            var button = Ui.Button(label, () => SwitchTab(id));
            button.ThemeTypeVariation = "TitleButton";
            button.Alignment = HorizontalAlignment.Left;
            column.AddChild(button);
            _tabButtons.Add(button);
        }
        _sidebar = Ui.Pad(Ui.MicaPanel(column, 10, 12), Ui.PanelGap, Ui.PanelGap, 0, 0);
        _sidebar.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _sidebar.Visible = false;
        body.AddChild(_sidebar);

        // Up and down stay inside the sidebar. Right is left to Godot, which finds the page content.
        Ui.ConfineVertical(_tabButtons);
        foreach (var button in _tabButtons) { button.FocusNeighborRight = new NodePath(); }
    }

    /// <summary>Opens a sidebar page. Choosing the page already showing moves focus into it.</summary>
    public void SwitchTab(string id)
    {
        if (Current?.Tab == id && _stack.Count == 1)
        {
            Current.FocusDefault();
            return;
        }
        var tab = Tabs.FirstOrDefault(t => t.Id == id);
        if (tab.Create != null) { ResetTo(tab.Create()); }
    }

    private void CycleTab(int direction)
    {
        var index = Array.FindIndex(Tabs, t => t.Id == Current?.Tab);
        if (index < 0) { return; }
        UiSounds.Play(UiSounds.Sound.Switch);
        SwitchTab(Tabs[(index + direction + Tabs.Length) % Tabs.Length].Id);
    }

    private void FocusSidebar()
    {
        var index = Array.FindIndex(Tabs, t => t.Id == Current?.Tab);
        if (index >= 0) { _tabButtons[index].GrabFocus(); }
    }

    private void BuildFooter(Control layout)
    {
        _hints = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore };
        _hints.AddThemeConstantOverride("separation", 32);
        _footer = Ui.Pad(Ui.MicaPanel(_hints, 24, 8), Ui.PanelGap, Ui.PanelGap, Ui.PanelGap, Ui.PanelGap);
        layout.AddChild(_footer);
    }

    /// <summary>Rebuilds the footer prompts from the current screen.</summary>
    public void RefreshChrome()
    {
        var screen = Current;
        _footer.Visible = screen?.ShowChrome ?? false;
        RefreshTopBar(screen);
        _sidebar.Visible = screen?.Tab != null;
        for (var i = 0; i < Tabs.Length; i++)
        {
            // The page being shown keeps an accent box. Focus is the gliding highlight on top of it.
            if (Tabs[i].Id == screen?.Tab) { _tabButtons[i].AddThemeStyleboxOverride("normal", Ui.Box(Ui.AccentDim, 10, padX: 18, padY: 8)); }
            else { _tabButtons[i].RemoveThemeStyleboxOverride("normal"); }
        }
        foreach (var child in _hints.GetChildren())
        {
            _hints.RemoveChild(child);
            child.QueueFree();
        }
        if (screen == null) { return; }
        foreach (var (action, label) in screen.Hints) { _hints.AddChild(Ui.Hint(action, label)); }
    }

    /// <summary>Applies a changed style, palette or background and rebuilds the current page with it.</summary>
    public void ReapplyAppearance()
    {
        Appearance.ApplySaved();
        var tab = Tabs.FirstOrDefault(t => t.Id == Current?.Tab);
        ResetTo(tab.Create != null ? tab.Create() : new HomeScreen());
    }

    private void BuildToast()
    {
        _toastLabel = Ui.Label("", 26);
        _toast = Ui.MicaPanel(_toastLabel, 28, 16);
        _toast.Visible = false;
        _toast.MouseFilter = MouseFilterEnum.Ignore;
        var holder = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        holder.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        holder.OffsetTop = 40;
        holder.OffsetBottom = 120;
        holder.AddChild(_toast);
        AddChild(holder);
    }

    public void Toast(string message)
    {
        _toastLabel.Text = message;
        _toast.Visible = true;
        _toast.Modulate = Colors.White;
        _toastTween?.Kill();
        _toastTween = CreateTween();
        _toastTween.TweenInterval(3.5);
        _toastTween.TweenProperty(_toast, "modulate:a", 0f, 0.4);
        _toastTween.TweenCallback(Callable.From(() => _toast.Visible = false));
    }

    public static bool IsFullscreen =>
        DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;

    public void ToggleFullscreen()
    {
        var full = IsFullscreen;
        DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
        Config.I.Fullscreen = !full;
    }

    // ---- Input ----

    public override void _Input(InputEvent e)
    {
        var overlay = TopOverlay;
        var handled = overlay != null ? overlay.OnRawInput(e) : Current?.OnRawInput(e) ?? false;
        if (handled) { GetViewport().SetInputAsHandled(); }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Actions.Fullscreen))
        {
            ToggleFullscreen();
            GetViewport().SetInputAsHandled();
            return;
        }

        var overlay = TopOverlay;
        if (overlay != null)
        {
            if (overlay.OnInput(e)) { GetViewport().SetInputAsHandled(); }
            else if (e.IsActionPressed("ui_cancel"))
            {
                CloseOverlay(overlay);
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        var screen = Current;
        if (screen == null) { return; }
        if (screen.OnInput(e)) { GetViewport().SetInputAsHandled(); }
        else if (e.IsActionPressed("ui_cancel"))
        {
            Back();
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed(Actions.Lb) || e.IsActionPressed(Actions.Rb))
        {
            // A tap switches sidebar page on release. A hold goes to the screen, as in RomBox.
            _shoulder = e.IsActionPressed(Actions.Lb) ? Actions.Lb : Actions.Rb;
            _shoulderFor = 0;
            GetViewport().SetInputAsHandled();
        }
        else if (_shoulder != null && e.IsActionReleased(_shoulder))
        {
            var direction = _shoulder == Actions.Lb ? -1 : 1;
            _shoulder = null;
            if (screen.Tab != null) { CycleTab(direction); }
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        _clockIn -= delta;
        if (_clockIn <= 0)
        {
            _clockIn = 5;
            RefreshClock();
        }

        if (_shoulder != null)
        {
            _shoulderFor += delta;
            if (_shoulderFor >= ShoulderHoldSeconds)
            {
                _shoulder = null;
                if (TopOverlay == null) { Current?.OnShoulderHold(); }
            }
        }

        var held = HeldPadDirection();
        if (held == null || held != _heldAction)
        {
            _heldAction = held;
            _heldFor = 0;
            _repeatIn = 0.4;
            return;
        }
        _heldFor += delta;
        _repeatIn -= delta;
        if (_repeatIn > 0) { return; }
        _repeatIn = _heldFor > 1.5 ? 0.05 : 0.1;
        Input.ParseInputEvent(new InputEventAction { Action = held, Pressed = true });
    }

    private static string HeldPadDirection()
    {
        foreach (var pad in Input.GetConnectedJoypads())
        {
            var x = Input.GetJoyAxis(pad, JoyAxis.LeftX);
            var y = Input.GetJoyAxis(pad, JoyAxis.LeftY);
            if (Input.IsJoyButtonPressed(pad, JoyButton.DpadLeft) || x < -0.6f) { return "ui_left"; }
            if (Input.IsJoyButtonPressed(pad, JoyButton.DpadRight) || x > 0.6f) { return "ui_right"; }
            if (Input.IsJoyButtonPressed(pad, JoyButton.DpadUp) || y < -0.6f) { return "ui_up"; }
            if (Input.IsJoyButtonPressed(pad, JoyButton.DpadDown) || y > 0.6f) { return "ui_down"; }
        }
        return null;
    }
}
