using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

namespace Padfin;

public class JellyfinException : Exception
{
    public HttpStatusCode? Status { get; }
    public JellyfinException(string message, HttpStatusCode? status = null) : base(message) { Status = status; }
}

/// <summary>Jellyfin REST client. One server and one signed-in user at a time.</summary>
public partial class Jellyfin : Node
{
    public const string ClientName = "Padfin";
    public const string ClientVersion = "0.1.2";
    private const string ItemFields = "Overview,Genres,ChildCount,PrimaryImageAspectRatio";

    public static Jellyfin I { get; private set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public HttpClient Http { get; } = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromSeconds(20) };

    public string Server { get; private set; } = "";
    public string Token { get; private set; } = "";
    public string UserId { get; private set; } = "";

    /// <summary>Raised when the server rejects the saved token.</summary>
    public event Action SessionExpired;

    public override void _EnterTree()
    {
        I = this;
        Server = Config.I.Server;
        Token = Config.I.Token;
        UserId = Config.I.UserId;
    }

    // ---- Session ----

    public string AuthHeader
    {
        get
        {
            var device = Uri.EscapeDataString(System.Environment.MachineName);
            var header = $"MediaBrowser Client=\"{ClientName}\", Device=\"{device}\", DeviceId=\"{Config.I.DeviceId}\", Version=\"{ClientVersion}\"";
            return Token == "" ? header : header + $", Token=\"{Token}\"";
        }
    }

    /// <summary>Header for mpv's own HTTP requests. It has no commas, which mpv treats as list separators.</summary>
    public string StreamAuthHeader => $"Authorization: MediaBrowser Token=\"{Token}\"";

    public static string NormalizeAddress(string address)
    {
        address = (address ?? "").Trim().TrimEnd('/');
        if (address == "") { return ""; }
        if (!address.Contains("://")) { address = "http://" + address; }
        return address;
    }

    /// <summary>Checks that a Jellyfin server answers at the address and selects it.</summary>
    public async Task<PublicSystemInfo> ConnectAsync(string address)
    {
        address = NormalizeAddress(address);
        if (!Uri.TryCreate(address, UriKind.Absolute, out _)) { throw new JellyfinException("That address is not valid."); }
        var previous = Server;
        Server = address;
        try
        {
            var info = await Get<PublicSystemInfo>("/System/Info/Public");
            if (info?.Id == null) { throw new JellyfinException("No Jellyfin server answered at that address."); }
            Config.I.Server = address;
            Config.I.ServerName = info.ServerName ?? address;
            return info;
        }
        catch
        {
            Server = previous;
            throw;
        }
    }

    public async Task LoginAsync(string username, string password)
    {
        var result = await Post<AuthResult>("/Users/AuthenticateByName", new { Username = username, Pw = password ?? "" });
        ApplySession(result);
    }

    public Task<bool> QuickConnectEnabledAsync() => Get<bool>("/QuickConnect/Enabled");
    public Task<QuickConnectResult> QuickConnectStartAsync() => Post<QuickConnectResult>("/QuickConnect/Initiate", null);
    public Task<QuickConnectResult> QuickConnectPollAsync(string secret) => Get<QuickConnectResult>("/QuickConnect/Connect", ("secret", secret));

    public async Task QuickConnectFinishAsync(string secret)
    {
        var result = await Post<AuthResult>("/Users/AuthenticateWithQuickConnect", new { Secret = secret });
        ApplySession(result);
    }

    /// <summary>Approves a Quick Connect code shown on another device, signing it in as this user.</summary>
    public Task<bool> AuthorizeQuickConnectAsync(string code) =>
        Post<bool>($"/QuickConnect/Authorize?code={Uri.EscapeDataString(code)}&userId={UserId}", null);

    public Task<List<UserDto>> PublicUsersAsync() => Get<List<UserDto>>("/Users/Public");

    /// <summary>True when the saved token still works.</summary>
    public async Task<bool> ValidateSessionAsync()
    {
        if (!Config.I.HasSession) { return false; }
        var user = await Get<UserDto>("/Users/Me");
        return user?.Id != null;
    }

    // ---- Preferences kept on the server ----

    /// <summary>
    /// The user's Jellyfin preferences, such as audio and subtitle language. They live on the server
    /// so they follow the user to every client. Null until loaded.
    /// </summary>
    public JsonObject UserPreferences { get; private set; }

    /// <summary>Loads the preferences once. Failure leaves them null and callers fall back to defaults.</summary>
    public async Task LoadPreferencesAsync(bool force = false)
    {
        if (UserPreferences != null && !force) { return; }
        try
        {
            var user = await Get<JsonObject>("/Users/Me");
            UserPreferences = user?["Configuration"]?.DeepClone() as JsonObject;
        }
        catch (JellyfinException e) { GD.Print("Preferences: ", e.Message); }
    }

    public string Preference(string name, string fallback = "") =>
        UserPreferences?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? fallback : fallback;

    public bool Preference(string name, bool fallback) =>
        UserPreferences?[name] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    /// <summary>Changes one preference and saves the whole set back to the server.</summary>
    public async Task SetPreferenceAsync(string name, JsonNode value)
    {
        if (UserPreferences == null) { throw new JellyfinException("Your preferences have not loaded from the server."); }
        UserPreferences[name] = value;
        await Post($"/Users/Configuration?userId={UserId}", UserPreferences);
    }

    public void SignOut()
    {
        UserPreferences = null;
        if (Token != "") { _ = SafeFire(() => Post("/Sessions/Logout", null)); }
        Token = "";
        UserId = "";
        Config.I.ClearSession();
        Seerr.I.Disconnect(); // The Seerr sign-in belongs to this person, so it goes with them.
    }

    private void ApplySession(AuthResult result)
    {
        if (result?.AccessToken == null || result.User == null) { throw new JellyfinException("The server did not return a session."); }
        Token = result.AccessToken;
        UserId = result.User.Id;
        Config.I.Token = Token;
        Config.I.UserId = UserId;
        Config.I.UserName = result.User.Name ?? "";
    }

    // ---- Library ----

    public async Task<List<BaseItem>> ViewsAsync() =>
        (await Get<ItemsResult>("/UserViews", ("userId", UserId))).Items;

    public async Task<List<BaseItem>> ResumeAsync(int limit = 20) =>
        (await Get<ItemsResult>("/UserItems/Resume", ("userId", UserId), ("limit", limit.ToString()),
            ("mediaTypes", "Video"), ("fields", ItemFields), ("enableImageTypes", "Primary,Backdrop,Thumb"))).Items;

    public async Task<List<BaseItem>> NextUpAsync(string seriesId = null, int limit = 20) =>
        (await Get<ItemsResult>("/Shows/NextUp", ("userId", UserId), ("limit", limit.ToString()),
            ("seriesId", seriesId), ("fields", ItemFields), ("enableResumable", "false"),
            ("enableRewatching", Config.I.NextUpRewatching ? "true" : "false"),
            ("nextUpDateCutoff", Config.I.NextUpDays > 0 ? DateTime.UtcNow.AddDays(-Config.I.NextUpDays).ToString("o") : null))).Items;

    public Task<List<BaseItem>> LatestAsync(string parentId, int limit = 20) =>
        Get<List<BaseItem>>("/Items/Latest", ("userId", UserId), ("parentId", parentId),
            ("limit", limit.ToString()), ("fields", ItemFields));

    public Task<ItemsResult> ItemsAsync(string parentId, string itemTypes, bool recursive, string sortBy, bool descending, int start, int limit, string genreId = null) =>
        Get<ItemsResult>("/Items", ("userId", UserId), ("parentId", parentId), ("includeItemTypes", itemTypes), ("genreIds", genreId),
            ("recursive", recursive ? "true" : "false"), ("sortBy", sortBy), ("sortOrder", descending ? "Descending" : "Ascending"),
            ("startIndex", start.ToString()), ("limit", limit.ToString()), ("fields", ItemFields));

    /// <summary>The genres that items of these types have, by name.</summary>
    public async Task<List<BaseItem>> GenresAsync(string parentId, string itemTypes) =>
        (await Get<ItemsResult>("/Genres", ("userId", UserId), ("parentId", parentId), ("includeItemTypes", itemTypes),
            ("sortBy", "SortName"), ("enableImages", "false"), ("enableTotalRecordCount", "false"))).Items;

    public Task<ItemsResult> SearchAsync(string term, int limit = 60) =>
        Get<ItemsResult>("/Items", ("userId", UserId), ("searchTerm", term), ("recursive", "true"),
            ("includeItemTypes", "Movie,Series,Episode,BoxSet"), ("limit", limit.ToString()), ("fields", ItemFields));

    public Task<BaseItem> ItemAsync(string id) => Get<BaseItem>($"/Items/{id}", ("userId", UserId));

    public async Task<List<BaseItem>> SeasonsAsync(string seriesId) =>
        (await Get<ItemsResult>($"/Shows/{seriesId}/Seasons", ("userId", UserId), ("fields", ItemFields))).Items;

    public async Task<List<BaseItem>> EpisodesAsync(string seriesId, string seasonId = null, string startItemId = null, int? limit = null) =>
        (await Get<ItemsResult>($"/Shows/{seriesId}/Episodes", ("userId", UserId), ("seasonId", seasonId),
            ("startItemId", startItemId), ("limit", limit?.ToString()), ("fields", ItemFields))).Items;

    public Task SetPlayedAsync(string itemId, bool played) =>
        played ? Post($"/UserPlayedItems/{itemId}?userId={UserId}", null) : Delete($"/UserPlayedItems/{itemId}?userId={UserId}");

    public Task SetFavoriteAsync(string itemId, bool favorite) =>
        favorite ? Post($"/UserFavoriteItems/{itemId}?userId={UserId}", null) : Delete($"/UserFavoriteItems/{itemId}?userId={UserId}");

    // ---- Images ----

    public string ImageUrl(string itemId, string type, int width, int height, string tag = null, int index = -1)
    {
        var path = index >= 0 ? $"/Items/{itemId}/Images/{type}/{index}" : $"/Items/{itemId}/Images/{type}";
        var url = $"{Server}{path}?fillWidth={width}&fillHeight={height}&quality=90";
        return tag == null ? url : url + "&tag=" + tag;
    }

    /// <summary>Portrait artwork. Episodes use their series poster.</summary>
    public string PosterUrl(BaseItem item, int width = 330, int height = 495)
    {
        if (item.IsEpisode && item.SeriesId != null) { return ImageUrl(item.SeriesId, "Primary", width, height, item.SeriesPrimaryImageTag); }
        return item.HasPrimary ? ImageUrl(item.Id, "Primary", width, height, item.ImageTags["Primary"]) : null;
    }

    /// <summary>Landscape artwork: an episode still, a backdrop, or the series backdrop.</summary>
    public string LandscapeUrl(BaseItem item, int width = 600, int height = 338)
    {
        if (item.IsEpisode && item.HasPrimary) { return ImageUrl(item.Id, "Primary", width, height, item.ImageTags["Primary"]); }
        if (item.ImageTags != null && item.ImageTags.TryGetValue("Thumb", out var thumb)) { return ImageUrl(item.Id, "Thumb", width, height, thumb); }
        if (item.HasBackdrop) { return ImageUrl(item.Id, "Backdrop", width, height, item.BackdropImageTags[0], 0); }
        if (item.ParentBackdropItemId != null && item.ParentBackdropImageTags is { Count: > 0 })
        {
            return ImageUrl(item.ParentBackdropItemId, "Backdrop", width, height, item.ParentBackdropImageTags[0], 0);
        }
        return item.HasPrimary ? ImageUrl(item.Id, "Primary", width, height, item.ImageTags["Primary"]) : null;
    }

    public string BackdropUrl(BaseItem item)
    {
        if (item.HasBackdrop) { return ImageUrl(item.Id, "Backdrop", 1920, 1080, item.BackdropImageTags[0], 0); }
        if (item.ParentBackdropItemId != null && item.ParentBackdropImageTags is { Count: > 0 })
        {
            return ImageUrl(item.ParentBackdropItemId, "Backdrop", 1920, 1080, item.ParentBackdropImageTags[0], 0);
        }
        return null;
    }

    /// <summary>
    /// The icon the server's web client publishes in its manifest, or null. Jellyfin has no API for
    /// a server icon, and the manifest is where a customised install keeps one.
    /// </summary>
    public async Task<string> ServerIconUrlAsync()
    {
        try
        {
            var text = await Http.GetStringAsync(Server + "/web/manifest.json");
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("icons", out var icons)) { return null; }
            string best = null;
            var bestSize = 0;
            foreach (var icon in icons.EnumerateArray())
            {
                if (!icon.TryGetProperty("src", out var src)) { continue; }
                var sizes = icon.TryGetProperty("sizes", out var value) ? value.GetString() ?? "" : "";
                int.TryParse(sizes.Split('x')[0], out var size);
                // Largest up to 256 pixels: sharp in the top bar without downloading a splash-sized image.
                if (best == null || (size > bestSize && size <= 256)) { (best, bestSize) = (src.GetString(), size); }
            }
            if (string.IsNullOrEmpty(best)) { return null; }
            return best.Contains("://") ? best : $"{Server}/web/{best.TrimStart('.', '/')}";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // ---- Playback ----

    /// <param name="forceTranscode">Asks for a converted stream even when the file could play as it is.</param>
    /// <param name="mediaSourceId">The version to play, for titles with more than one.</param>
    public Task<PlaybackInfo> PlaybackInfoAsync(string itemId, bool forceTranscode = false, string mediaSourceId = null) =>
        Post<PlaybackInfo>($"/Items/{itemId}/PlaybackInfo?userId={UserId}", new
        {
            UserId,
            MediaSourceId = mediaSourceId,
            MaxStreamingBitrate = MaxBitrate,
            AutoOpenLiveStream = true,
            EnableDirectPlay = !forceTranscode,
            EnableDirectStream = !forceTranscode,
            DeviceProfile = MpvDeviceProfile(forceTranscode),
        });

    /// <summary>The bitrate ceiling from settings, in bits per second. Above it the server transcodes.</summary>
    private static int MaxBitrate => Config.I.MaxBitrateMbps > 0 ? Config.I.MaxBitrateMbps * 1_000_000 : 400_000_000;

    /// <summary>mpv decodes every container and codec Jellyfin stores, so the profile asks for direct play of everything.</summary>
    private static object MpvDeviceProfile(bool forceTranscode) => new
    {
        Name = "Padfin (mpv)",
        MaxStreamingBitrate = MaxBitrate,
        MaxStaticBitrate = MaxBitrate,
        DirectPlayProfiles = forceTranscode ? Array.Empty<object>() : new object[] { new { Type = "Video" }, new { Type = "Audio" } },
        TranscodingProfiles = new object[]
        {
            new { Type = "Video", Container = "ts", Protocol = "hls", VideoCodec = "h264,hevc", AudioCodec = "aac,ac3,eac3,opus", Context = "Streaming", MaxAudioChannels = "6" },
        },
        SubtitleProfiles = new[] { "srt", "subrip", "ass", "ssa", "vtt", "webvtt", "sub", "smi", "ttml" }
            .Select(f => (object)new { Format = f, Method = "External" })
            .Concat(new[] { "srt", "subrip", "ass", "ssa", "pgssub", "pgs", "dvdsub", "dvbsub", "mov_text", "vtt" }
                .Select(f => (object)new { Format = f, Method = "Embed" }))
            .ToArray(),
        ContainerProfiles = Array.Empty<object>(),
        CodecProfiles = Array.Empty<object>(),
    };

    public string DirectStreamUrl(string itemId, string mediaSourceId, string playSessionId) =>
        $"{Server}/Videos/{itemId}/stream?static=true&mediaSourceId={mediaSourceId}&playSessionId={playSessionId}&deviceId={Config.I.DeviceId}";

    /// <summary>One sheet of scrubbing thumbnails.</summary>
    public string TrickplayUrl(string itemId, string mediaSourceId, int width, int sheet) =>
        $"{Server}/Videos/{itemId}/Trickplay/{width}/{sheet}.jpg?mediaSourceId={mediaSourceId}";

    public string ExternalSubtitleUrl(string itemId, string mediaSourceId, MediaStream stream)
    {
        var ext = (stream.Codec ?? "").ToLowerInvariant() switch
        {
            "ass" or "ssa" => "ass",
            "vtt" or "webvtt" => "vtt",
            _ => "srt",
        };
        return $"{Server}/Videos/{itemId}/{mediaSourceId}/Subtitles/{stream.Index}/0/Stream.{ext}";
    }

    public async Task<List<MediaSegment>> SegmentsAsync(string itemId)
    {
        try { return (await Get<MediaSegmentResult>($"/MediaSegments/{itemId}")).Items ?? new(); }
        catch (JellyfinException) { return new(); } // Servers before 10.10 have no segments endpoint.
    }

    public Task ReportStartAsync(object body) => Post("/Sessions/Playing", body);
    public Task ReportProgressAsync(object body) => Post("/Sessions/Playing/Progress", body);
    public Task ReportStoppedAsync(object body) => Post("/Sessions/Playing/Stopped", body);
    public Task StopEncodingAsync(string playSessionId) =>
        Delete($"/Videos/ActiveEncodings?deviceId={Config.I.DeviceId}&playSessionId={playSessionId}");

    // ---- Discovery ----

    /// <summary>Finds servers on the local network with Jellyfin's UDP broadcast on port 7359.</summary>
    public static async Task<List<DiscoveredServer>> DiscoverAsync(int milliseconds = 2500)
    {
        var found = new Dictionary<string, DiscoveredServer>();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            var probe = Encoding.UTF8.GetBytes("who is JellyfinServer?");
            await udp.SendAsync(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, 7359));
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < deadline)
            {
                var receive = udp.ReceiveAsync();
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || await Task.WhenAny(receive, Task.Delay(remaining)) != receive) { break; }
                try
                {
                    var server = JsonSerializer.Deserialize<DiscoveredServer>(Encoding.UTF8.GetString(receive.Result.Buffer), Json);
                    if (server?.Address != null) { found[server.Id ?? server.Address] = server; }
                }
                catch (JsonException) { }
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
        {
            GD.Print("Server discovery failed: ", e.Message);
        }
        return found.Values.ToList();
    }

    // ---- HTTP ----

    private static async Task SafeFire(Func<Task> call)
    {
        try { await call(); } catch (Exception) { }
    }

    public Task<T> Get<T>(string path, params (string Key, string Value)[] query) =>
        Send<T>(HttpMethod.Get, path + QueryString(query), null);

    public Task<T> Post<T>(string path, object body) => Send<T>(HttpMethod.Post, path, body);
    public Task Post(string path, object body) => Send<object>(HttpMethod.Post, path, body);
    public Task Delete(string path) => Send<object>(HttpMethod.Delete, path, null);

    private static string QueryString((string Key, string Value)[] query)
    {
        var parts = query.Where(q => !string.IsNullOrEmpty(q.Value))
            .Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}").ToList();
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object body)
    {
        if (Server == "") { throw new JellyfinException("No server selected."); }

        HttpResponseMessage response = null;
        // A GET is safe to repeat, so one dropped connection does not surface as an error.
        for (var attempt = 0; response == null; attempt++)
        {
            using var request = new HttpRequestMessage(method, Server + path);
            request.Headers.TryAddWithoutValidation("Authorization", AuthHeader);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (body != null) { request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"); }
            else if (method == HttpMethod.Post) { request.Content = new StringContent("", Encoding.UTF8, "application/json"); }

            try { response = await Http.SendAsync(request); }
            catch (HttpRequestException) when (method == HttpMethod.Get && attempt == 0) { }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                GD.Print($"{method} {path} failed: {e.GetType().Name}: {e.Message}");
                throw new JellyfinException("Could not reach the server.");
            }
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var hadSession = Token != "" && !path.StartsWith("/Users/Authenticate");
                if (hadSession) { SessionExpired?.Invoke(); }
                throw new JellyfinException(hadSession ? "Your session has expired." : "Wrong username or password.", response.StatusCode);
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new JellyfinException($"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.", response.StatusCode);
            }
            var text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text)) { return default; }
            try { return JsonSerializer.Deserialize<T>(text, Json); }
            catch (JsonException) { throw new JellyfinException("The server sent a reply Padfin could not read."); }
        }
    }
}
