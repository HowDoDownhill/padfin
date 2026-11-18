using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>A Seerr title: what it is, whether the library has it, and the buttons to request it.</summary>
public partial class SeerrDetailsScreen : Screen
{
    private SeerrItem _item;
    private TextureRect _backdrop;
    private TextureRect _poster;
    private VBoxContainer _info;
    private HFlowContainer _buttons;
    private bool _busy;

    public override IEnumerable<(string, string)> Hints => new[] { (Actions.Accept, "Select"), (Actions.Back, "Back") };

    public SeerrDetailsScreen() { }
    public SeerrDetailsScreen(SeerrItem item) { _item = item; }

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
        var pad = Ui.Pad(top, Ui.Margin, 50, Ui.Margin, 0);
        Ui.Fill(pad);
        AddChild(pad);

        BuildInfo();
        LoadArt();
        _ = LoadAsync();
    }

    public override void FocusDefault()
    {
        if (_buttons != null && _buttons.GetChildCount() > 0) { ((Control)_buttons.GetChild(0)).GrabFocus(); }
    }

    private async void LoadArt()
    {
        var poster = await ImageCache.I.GetAsync(Seerr.I.PosterUrl(_item));
        if (!IsInstanceValid(this)) { return; }
        _poster.Texture = poster;
        if (!Config.I.Backdrops) { return; }
        var backdrop = await ImageCache.I.GetAsync(Seerr.I.BackdropUrl(_item));
        if (IsInstanceValid(this)) { _backdrop.Texture = backdrop; }
    }

    /// <summary>Fetches the full details, which carry seasons and the current request state.</summary>
    private async Task LoadAsync()
    {
        try
        {
            var details = await Seerr.I.DetailsAsync(_item.MediaType, _item.Id);
            if (!IsInstanceValid(this) || details == null) { return; }
            _item = details;
            var hadFocus = _buttons != null && GetViewport().GuiGetFocusOwner() is { } owner && _buttons.IsAncestorOf(owner);
            BuildInfo();
            if (App.I.Current == this && (hadFocus || GetViewport().GuiGetFocusOwner() == null)) { FocusDefault(); }
        }
        catch (SeerrException e) { App.I.Toast(e.Message); }
    }

    private void BuildInfo()
    {
        foreach (var child in _info.GetChildren())
        {
            _info.RemoveChild(child);
            child.QueueFree();
        }

        var title = Ui.Label(_item.DisplayTitle, 60);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _info.AddChild(title);

        var meta = new List<string> { _item.IsMovie ? "Movie" : "Show" };
        if (_item.Year != "") { meta.Add(_item.Year); }
        if (_item.Runtime is > 0) { meta.Add($"{_item.Runtime}m"); }
        if (_item.VoteAverage is > 0) { meta.Add($"★ {_item.VoteAverage:0.0}"); }
        var seasons = Seasons();
        if (seasons.Count > 0) { meta.Add(seasons.Count == 1 ? "1 season" : $"{seasons.Count} seasons"); }
        if (_item.Genres is { Count: > 0 }) { meta.Add(string.Join(", ", _item.Genres.Take(3).Select(g => g.Name))); }
        _info.AddChild(Ui.Label(string.Join("   ·   ", meta), 26, variation: "CaptionLabel"));

        foreach (var line in StatusLines()) { _info.AddChild(Ui.Label(line, 28, variation: "AccentLabel")); }

        var overview = Ui.Label(_item.Overview ?? "", 26);
        overview.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        overview.MaxLinesVisible = 6;
        overview.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        overview.CustomMinimumSize = new Vector2(600, 0);
        _info.AddChild(overview);
        _info.AddChild(Ui.Spacer(0, 6));

        _buttons = new HFlowContainer();
        _buttons.AddThemeConstantOverride("h_separation", 16);
        _buttons.AddThemeConstantOverride("v_separation", 12);
        _info.AddChild(_buttons);
        BuildButtons(seasons);
    }

    private List<SeerrSeason> Seasons() =>
        (_item.Seasons ?? new()).Where(s => s.SeasonNumber > 0).OrderBy(s => s.SeasonNumber).ToList();

    /// <summary>Requests still in play for the standard or the 4K copy: waiting for approval, or approved.</summary>
    private List<SeerrRequest> ActiveRequests(bool is4k) =>
        (_item.MediaInfo?.Requests ?? new())
            .Where(r => r.Is4k == is4k && r.Status is SeerrRequest.PendingApproval or SeerrRequest.Approved).ToList();

    private IEnumerable<string> StatusLines()
    {
        var media = _item.MediaInfo;
        if (media == null) { yield break; }
        foreach (var is4k in new[] { false, true })
        {
            var prefix = is4k ? "4K: " : "";
            var status = media.StatusFor(is4k);
            if (status == SeerrMedia.Available) { yield return prefix + "Available"; }
            else if (status == SeerrMedia.PartiallyAvailable) { yield return prefix + "Partially available"; }
            else if (ActiveRequests(is4k).FirstOrDefault() is { } request)
            {
                var who = request.RequestedBy?.Id == Seerr.I.User?.Id ? "you" : request.RequestedBy?.Name ?? "someone";
                yield return prefix + (request.Status == SeerrRequest.PendingApproval
                    ? $"Requested by {who}, waiting for approval"
                    : $"Requested by {who}, processing");
            }
        }
    }

    private void BuildButtons(List<SeerrSeason> seasons)
    {
        var media = _item.MediaInfo;
        var libraryId = media?.Status is SeerrMedia.Available or SeerrMedia.PartiallyAvailable ? media.JellyfinMediaId
            : media?.Status4k is SeerrMedia.Available or SeerrMedia.PartiallyAvailable ? media.JellyfinMediaId4k : null;
        if (!string.IsNullOrEmpty(libraryId)) { AddButton("Open in Library", () => _ = OpenInLibraryAsync(libraryId)); }

        var canRequest = _item.IsMovie ? Seerr.I.CanRequestMovies : Seerr.I.CanRequestTv;
        if (canRequest) { AddRequestButtons(seasons, false); }
        if (Seerr.I.CanRequest4k(_item.IsMovie)) { AddRequestButtons(seasons, true); }
        if (!canRequest && media?.Status != SeerrMedia.Available)
        {
            _info.AddChild(Ui.Label("Your Seerr account is not allowed to make requests.", 22, variation: "WarningLabel"));
        }

        var pending = (media?.Requests ?? new()).Where(r => r.Status == SeerrRequest.PendingApproval).ToList();
        if (Seerr.I.CanManageRequests)
        {
            // One pair of buttons per waiting request, naming the copy when both are waiting.
            foreach (var request in pending)
            {
                var suffix = pending.Count > 1 ? (request.Is4k ? " 4K" : " Standard") : "";
                AddButton("Approve" + suffix, () => _ = ManageAsync(() => Seerr.I.ApproveAsync(request.Id), "Approved."));
                AddButton("Decline" + suffix, () => _ = ConfirmThenManageAsync("Decline this request?",
                    "The person who asked will see it was declined.", "Hold to decline", () => Seerr.I.DeclineAsync(request.Id), "Declined."));
            }
        }
        var mine = pending.FirstOrDefault(r => r.RequestedBy?.Id == Seerr.I.User?.Id);
        if (mine != null) { AddButton("Cancel Request", () => _ = ConfirmThenManageAsync("Cancel your request?",
            "You can ask for it again later.", "Hold to cancel request", () => Seerr.I.CancelAsync(mine.Id), "Request cancelled.", "Keep Request")); }
    }

    private async Task ConfirmThenManageAsync(string title, string body, string holdLabel, Func<Task> action, string done, string keepLabel = "Cancel")
    {
        if (await App.I.ConfirmHoldAsync(title, body, holdLabel, keepLabel)) { await ManageAsync(action, done); }
    }

    private void AddButton(string text, Action pressed) => _buttons.AddChild(Ui.ActionButton(text, pressed));

    private void AddRequestButtons(List<SeerrSeason> seasons, bool is4k)
    {
        var quality = is4k ? " in 4K" : "";
        if (_item.MediaInfo?.StatusFor(is4k) == SeerrMedia.Available) { return; }
        if (_item.IsMovie)
        {
            if (ActiveRequests(is4k).Count == 0) { AddButton("Request" + quality, () => _ = RequestAsync(null, is4k)); }
            return;
        }

        // Seasons already asked for are left out of the choices.
        var taken = ActiveRequests(is4k).SelectMany(r => r.Seasons ?? new()).Select(s => s.SeasonNumber).ToHashSet();
        var open = seasons.Where(s => !taken.Contains(s.SeasonNumber)).ToList();
        if (open.Count == 0) { return; }
        var all = open.Count == seasons.Count ? "Request All Seasons" : "Request Remaining Seasons";
        AddButton(all + quality, () => _ = RequestAsync(open.Select(s => s.SeasonNumber).ToList(), is4k));
        if (open.Count > 1)
        {
            AddButton("Choose a Season" + quality, () =>
            {
                var items = open.Select(s => (s.Name ?? $"Season {s.SeasonNumber}", (Action)(() => _ = RequestAsync(new List<int> { s.SeasonNumber }, is4k)))).ToList();
                App.I.ShowMenu("Request a Season" + quality, items);
            });
        }
    }

    private async Task RequestAsync(List<int> seasons, bool is4k)
    {
        if (_busy) { return; }
        _busy = true;
        try
        {
            var options = new SeerrRequestOptions { Is4k = is4k };
            if (Seerr.I.CanRequestAdvanced && !await ChooseOptionsAsync(options)) { return; }
            var request = await Seerr.I.RequestAsync(_item, seasons, options);
            App.I.Toast(request?.Status == SeerrRequest.PendingApproval ? "Requested. Waiting for approval." : "Requested.");
            await LoadAsync();
        }
        catch (SeerrException e) { App.I.Toast(e.Message); }
        finally { _busy = false; }
    }

    /// <summary>
    /// Lets an account with advanced rights pick the server, quality profile and folder before the
    /// request is sent. False when the user backs out.
    /// </summary>
    private async Task<bool> ChooseOptionsAsync(SeerrRequestOptions options)
    {
        var servers = await Seerr.I.ServicesAsync(_item.IsMovie, options.Is4k);
        if (servers.Count == 0) { return true; } // Nothing to choose between. Seerr applies its defaults.

        var server = servers[0];
        var details = await Seerr.I.ServiceDetailsAsync(_item.IsMovie, server.Id);
        Use(server);

        while (true)
        {
            var profile = details.Profiles.FirstOrDefault(p => p.Id == options.ProfileId)?.Name ?? "Default";
            var labels = new List<string> { "Send Request", $"Quality: {profile}", $"Folder: {options.RootFolder ?? "Default"}" };
            if (servers.Count > 1) { labels.Add($"Server: {server.Name}"); }

            switch (await App.I.ChooseAsync("Request Options", labels))
            {
                case 0:
                    return true;
                case 1 when details.Profiles.Count > 0:
                    var picked = await App.I.ChooseAsync("Quality Profile", details.Profiles.Select(p => p.Name).ToList());
                    if (picked >= 0) { options.ProfileId = details.Profiles[picked].Id; }
                    break;
                case 2 when details.RootFolders.Count > 0:
                    var folder = await App.I.ChooseAsync("Folder", details.RootFolders.Select(f => f.Path).ToList());
                    if (folder >= 0) { options.RootFolder = details.RootFolders[folder].Path; }
                    break;
                case 3:
                    var chosen = await App.I.ChooseAsync("Server", servers.Select(s => s.Name).ToList());
                    if (chosen >= 0 && servers[chosen].Id != server.Id)
                    {
                        server = servers[chosen];
                        details = await Seerr.I.ServiceDetailsAsync(_item.IsMovie, server.Id);
                        Use(server);
                    }
                    break;
                case -1:
                    return false;
            }
        }

        void Use(SeerrService chosen)
        {
            options.ServerId = chosen.Id;
            options.ProfileId = chosen.ActiveProfileId;
            options.RootFolder = chosen.ActiveDirectory;
        }
    }

    /// <summary>Runs an approve, decline or cancel, then shows the title's new state.</summary>
    private async Task ManageAsync(Func<Task> action, string done)
    {
        if (_busy) { return; }
        _busy = true;
        try
        {
            await action();
            App.I.Toast(done);
            await LoadAsync();
        }
        catch (SeerrException e) { App.I.Toast(e.Message); }
        finally { _busy = false; }
    }

    private async Task OpenInLibraryAsync(string jellyfinId)
    {
        try { App.I.Open(await Jellyfin.I.ItemAsync(jellyfinId)); }
        catch (JellyfinException e) { App.I.Toast(e.Message); }
    }
}
