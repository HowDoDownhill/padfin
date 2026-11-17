using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

namespace Padfin;

public class SeerrException : Exception
{
    public SeerrException(string message) : base(message) { }
}

/// <summary>
/// Seerr client for finding and requesting titles. Signs in with a Jellyfin account, a Seerr
/// local account, or an API key, and repeats the sign-in on each launch from saved credentials.
/// </summary>
public partial class Seerr : Node
{
    public const string AuthJellyfin = "jellyfin";
    public const string AuthLocal = "local";
    public const string AuthApiKey = "apikey";

    private const int PermissionAdmin = 2;
    private const int PermissionManageRequests = 16;
    private const int PermissionRequest = 32;
    private const int PermissionRequest4k = 1024;
    private const int PermissionRequest4kMovie = 2048;
    private const int PermissionRequest4kTv = 4096;
    private const int PermissionRequestAdvanced = 8192;
    private const int PermissionRequestMovie = 262144;
    private const int PermissionRequestTv = 524288;

    public static Seerr I { get; private set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private HttpClient _http;
    private string _server = "";
    private string _apiKey;
    private bool _cacheImages;
    private SeerrPublicSettings _settings = new();
    private Task _restore = Task.CompletedTask;

    public SeerrUser User { get; private set; }
    public bool Connected => User != null;
    public bool Configured => Config.I.SeerrUrl != "";

    public bool CanRequestMovies => Has(PermissionRequest) || Has(PermissionRequestMovie);
    public bool CanRequestTv => Has(PermissionRequest) || Has(PermissionRequestTv);

    /// <summary>May approve and decline other people's requests.</summary>
    public bool CanManageRequests => Has(PermissionManageRequests);

    /// <summary>May choose the server, quality profile and folder for a request.</summary>
    public bool CanRequestAdvanced => Has(PermissionRequestAdvanced) || Has(PermissionManageRequests);

    /// <summary>The server offers 4K for this kind of title and the account may ask for it.</summary>
    public bool CanRequest4k(bool movie) =>
        (movie ? _settings.Movie4kEnabled : _settings.Series4kEnabled)
        && (Has(PermissionRequest4k) || Has(movie ? PermissionRequest4kMovie : PermissionRequest4kTv));

    private bool Has(int permission) =>
        User != null && ((User.Permissions & PermissionAdmin) != 0 || (User.Permissions & permission) != 0);

    public override void _EnterTree() => I = this;

    // ---- Connection ----

    /// <summary>Signs in again with the saved credentials. Safe to await more than once.</summary>
    public Task RestoreAsync()
    {
        if (Connected || !Configured) { return Task.CompletedTask; }
        if (_restore.IsCompleted)
        {
            _restore = Quiet(() => ConnectAsync(Config.I.SeerrUrl, Config.I.SeerrAuth, Config.I.SeerrUser, Config.I.SeerrSecret, false));
        }
        return _restore;
    }

    private static async Task Quiet(Func<Task> call)
    {
        try { await call(); }
        catch (SeerrException e) { GD.Print("Seerr: ", e.Message); }
    }

    /// <summary>Connects and signs in. Saves the details when it works.</summary>
    public async Task ConnectAsync(string address, string auth, string username, string secret, bool save = true)
    {
        address = Jellyfin.NormalizeAddress(address);
        if (!Uri.TryCreate(address, UriKind.Absolute, out _)) { throw new SeerrException("That address is not valid."); }

        // A fresh client per sign-in, so an old session cookie never mixes with a new account.
        _http?.Dispose();
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        })
        { Timeout = TimeSpan.FromSeconds(20) };
        _server = address;
        _apiKey = auth == AuthApiKey ? secret : null;
        User = null;

        try
        {
            var settings = await Send<SeerrPublicSettings>(HttpMethod.Get, "/settings/public", null, false);
            _settings = settings ?? new SeerrPublicSettings();
            _cacheImages = _settings.CacheImages;
            if (auth == AuthJellyfin) { await Send<SeerrUser>(HttpMethod.Post, "/auth/jellyfin", new { username, password = secret ?? "" }, false); }
            else if (auth == AuthLocal) { await Send<SeerrUser>(HttpMethod.Post, "/auth/local", new { email = username, password = secret ?? "" }, false); }
            User = await Send<SeerrUser>(HttpMethod.Get, "/auth/me", null, false);
            if (User == null) { throw new SeerrException("Seerr did not return an account."); }
        }
        catch
        {
            User = null;
            throw;
        }

        if (save)
        {
            Config.I.SeerrUrl = address;
            Config.I.SeerrAuth = auth;
            Config.I.SeerrUser = username ?? "";
            Config.I.SeerrSecret = secret ?? "";
        }
    }

    public void Disconnect()
    {
        User = null;
        _server = "";
        _apiKey = null;
        Config.I.SeerrUrl = "";
        Config.I.SeerrUser = "";
        Config.I.SeerrSecret = "";
    }

    // ---- Titles ----

    /// <summary>Movies and shows matching a search, with what Seerr knows about each.</summary>
    public Task<List<SeerrItem>> SearchAsync(string query) => ListAsync("/search?query=" + Uri.EscapeDataString(query));

    private async Task<List<SeerrItem>> ListAsync(string path)
    {
        var page = await Send<SeerrPage>(HttpMethod.Get, path, null);
        return (page?.Results ?? new()).Where(r => r.MediaType is "movie" or "tv").ToList();
    }

    public async Task<SeerrItem> DetailsAsync(string mediaType, int tmdbId)
    {
        var item = await Send<SeerrItem>(HttpMethod.Get, $"/{(mediaType == "movie" ? "movie" : "tv")}/{tmdbId}", null);
        if (item != null) { item.MediaType = mediaType; }
        return item;
    }

    // ---- Requests ----

    /// <summary>Asks for a movie, or for the given seasons of a show. Null seasons means every season.</summary>
    public Task<SeerrRequest> RequestAsync(SeerrItem item, IReadOnlyList<int> seasons = null, SeerrRequestOptions options = null)
    {
        var body = new Dictionary<string, object> { ["mediaType"] = item.IsMovie ? "movie" : "tv", ["mediaId"] = item.Id };
        if (!item.IsMovie) { body["seasons"] = seasons == null ? "all" : seasons; }
        if (options != null)
        {
            if (options.Is4k) { body["is4k"] = true; }
            if (options.ServerId != null) { body["serverId"] = options.ServerId; }
            if (options.ProfileId != null) { body["profileId"] = options.ProfileId; }
            if (!string.IsNullOrEmpty(options.RootFolder)) { body["rootFolder"] = options.RootFolder; }
        }
        return Send<SeerrRequest>(HttpMethod.Post, "/request", body);
    }

    public Task ApproveAsync(int requestId) => Send<object>(HttpMethod.Post, $"/request/{requestId}/approve", null);
    public Task DeclineAsync(int requestId) => Send<object>(HttpMethod.Post, $"/request/{requestId}/decline", null);

    /// <summary>The Radarr or Sonarr servers that take standard or 4K requests.</summary>
    public async Task<List<SeerrService>> ServicesAsync(bool movie, bool is4k)
    {
        var all = await Send<List<SeerrService>>(HttpMethod.Get, movie ? "/service/radarr" : "/service/sonarr", null) ?? new();
        return all.Where(s => s.Is4k == is4k).OrderByDescending(s => s.IsDefault).ToList();
    }

    /// <summary>A server's quality profiles and root folders.</summary>
    public Task<SeerrServiceDetails> ServiceDetailsAsync(bool movie, int serverId) =>
        Send<SeerrServiceDetails>(HttpMethod.Get, $"/service/{(movie ? "radarr" : "sonarr")}/{serverId}", null);

    public Task CancelAsync(int requestId) => Send<object>(HttpMethod.Delete, $"/request/{requestId}", null);

    /// <summary>Recent requests this account can see, newest first, each with its title details filled in.</summary>
    public async Task<List<(SeerrRequest Request, SeerrItem Item)>> RequestsAsync(int take = 40)
    {
        var page = await Send<SeerrRequestPage>(HttpMethod.Get, $"/request?take={take}&skip=0&filter=all&sort=added", null);
        var requests = (page?.Results ?? new()).Where(r => r.Media != null).ToList();
        // The request list carries only TMDB ids. Titles and posters come from each title's details.
        var details = await Task.WhenAll(requests.Select(async r =>
        {
            try { return await DetailsAsync(r.Media.MediaType ?? r.Type, r.Media.TmdbId); }
            catch (SeerrException) { return null; }
        }));
        return requests.Select((r, i) => (r, details[i])).Where(p => p.Item2 != null).ToList();
    }

    // ---- Images ----

    public string PosterUrl(SeerrItem item) => ImageUrl("/t/p/w500", item.PosterPath);
    public string BackdropUrl(SeerrItem item) => ImageUrl("/t/p/w1920_and_h1080_multi_faces", item.BackdropPath);

    /// <summary>TMDB artwork, through Seerr's image proxy when the server caches images.</summary>
    private string ImageUrl(string size, string path)
    {
        if (string.IsNullOrEmpty(path)) { return null; }
        return (_cacheImages ? _server + "/imageproxy/tmdb" : "https://image.tmdb.org") + size + path;
    }

    // ---- HTTP ----

    private async Task<T> Send<T>(HttpMethod method, string path, object body, bool retrySignIn = true)
    {
        if (_http == null || _server == "") { throw new SeerrException("Seerr is not connected."); }
        using var request = new HttpRequestMessage(method, _server + "/api/v1" + path);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (_apiKey != null) { request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey); }
        if (body != null) { request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"); }

        HttpResponseMessage response;
        try { response = await _http.SendAsync(request); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            throw new SeerrException("Could not reach Seerr.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // A session cookie that expired mid-session: sign in again once and repeat the call.
                if (retrySignIn && _apiKey == null && Configured && User != null)
                {
                    await ConnectAsync(Config.I.SeerrUrl, Config.I.SeerrAuth, Config.I.SeerrUser, Config.I.SeerrSecret, false);
                    return await Send<T>(method, path, body, false);
                }
                throw new SeerrException(path.StartsWith("/auth/") ? "Seerr did not accept that sign-in." : ErrorText(text) ?? "Seerr does not allow that for your account.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new SeerrException(ErrorText(text) ?? $"Seerr answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            if (string.IsNullOrWhiteSpace(text)) { return default; }
            try { return JsonSerializer.Deserialize<T>(text, Json); }
            catch (JsonException) { throw new SeerrException("Seerr sent a reply Padfin could not read."); }
        }
    }

    private static string ErrorText(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<SeerrError>(body, Json);
            return error?.Message ?? error?.Error;
        }
        catch (JsonException) { return null; }
    }
}
