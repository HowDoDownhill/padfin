using System;
using Godot;

namespace Padfin;

/// <summary>Settings and the saved session, stored in user://padfin.cfg.</summary>
public partial class Config : Node
{
    public static Config I { get; private set; }

    // Normally user://padfin.cfg. "-- --config <file>" points at another file, so scripted tests
    // never touch the real settings and sign-in.
    private string Path = "user://padfin.cfg";
    private readonly ConfigFile _file = new();

    public string Server { get => Get("session", "server", ""); set => Set("session", "server", value); }
    public string ServerName { get => Get("session", "server_name", ""); set => Set("session", "server_name", value); }
    public string Token { get => Get("session", "token", ""); set => Set("session", "token", value); }
    public string UserId { get => Get("session", "user_id", ""); set => Set("session", "user_id", value); }
    public string UserName { get => Get("session", "user_name", ""); set => Set("session", "user_name", value); }

    public bool Fullscreen { get => Get("display", "fullscreen", "true") == "true"; set => Set("display", "fullscreen", value ? "true" : "false"); }

    /// <summary>False once the user picks Don't Ask Again on the offer to add Padfin to Steam.</summary>
    public bool OfferSteamLaunch { get => GetBool("steam", "offer", true); set => SetBool("steam", "offer", value); }

    /// <summary>
    /// Offer test builds, which GitHub calls pre-releases, as updates. On by default while Padfin
    /// has only test builds, so the first installs keep receiving them.
    /// </summary>
    public bool PreReleaseBuilds { get => GetBool("updates", "test_builds", true); set => SetBool("updates", "test_builds", value); }
    /// <summary>The test build from which the user declined to go back to a full release.</summary>
    public string DeclinedGoBackFrom { get => Get("updates", "declined_go_back_from", ""); set => Set("updates", "declined_go_back_from", value); }

    // Appearance, with the same names and defaults RomBox uses.
    public string AppStyle { get => Get("ui", "style", "Glass"); set => Set("ui", "style", value); }
    public string AppTheme { get => Get("ui", "theme", Palettes.DefaultName); set => Set("ui", "theme", value); }
    public string AppBackground { get => Get("ui", "background", "Flow"); set => Set("ui", "background", value); }
    public string InterfaceSize { get => Get("ui", "interface_size", "Auto"); set => Set("ui", "interface_size", value); }
    public bool UiSounds { get => Get("ui", "sounds", "true") == "true"; set => Set("ui", "sounds", value ? "true" : "false"); }

    // Seerr connection. The secret is a password or an API key, stored in plain text like the Jellyfin token.
    public string SeerrUrl { get => Get("seerr", "url", ""); set => Set("seerr", "url", value); }
    public string SeerrAuth { get => Get("seerr", "auth", Seerr.AuthJellyfin); set => Set("seerr", "auth", value); }
    public string SeerrUser { get => Get("seerr", "user", ""); set => Set("seerr", "user", value); }
    public string SeerrSecret { get => Get("seerr", "secret", ""); set => Set("seerr", "secret", value); }

    /// <summary>mpv hwdec value. "auto-safe" uses the GPU decoder when mpv trusts it.</summary>
    public string HardwareDecoding { get => Get("playback", "hwdec", "auto-safe"); set => Set("playback", "hwdec", value); }

    /// <summary>Highest bitrate to ask the server for, in megabits per second. Zero means no limit.</summary>
    public int MaxBitrateMbps { get => GetInt("playback", "max_bitrate_mbps", 0); set => SetInt("playback", "max_bitrate_mbps", value); }
    public int SkipForwardSeconds { get => GetInt("playback", "skip_forward", 30); set => SetInt("playback", "skip_forward", value); }
    public int SkipBackSeconds { get => GetInt("playback", "skip_back", 10); set => SetInt("playback", "skip_back", value); }

    /// <summary>Megabytes of video kept in memory ahead of the playback position.</summary>
    public int BufferMegabytes { get => GetInt("playback", "buffer_mb", 200); set => SetInt("playback", "buffer_mb", value); }
    /// <summary>How far LB and RB jump during playback.</summary>
    public int LongSkipSeconds { get => GetInt("playback", "long_skip", 300); set => SetInt("playback", "long_skip", value); }
    /// <summary>Seconds to step back from the saved position when resuming.</summary>
    public int ResumeRewindSeconds { get => GetInt("playback", "resume_rewind", 0); set => SetInt("playback", "resume_rewind", value); }
    public bool PlaybackStats { get => GetBool("playback", "stats", false); set => SetBool("playback", "stats", value); }

    // Picture. Each value is what mpv takes for the option of the same purpose.
    public string VideoZoom { get => Get("video", "zoom", "fit"); set => Set("video", "zoom", value); }
    public string VideoScaling { get => Get("video", "scaling", "default"); set => Set("video", "scaling", value); }
    public string ToneMapping { get => Get("video", "tone_mapping", "auto"); set => Set("video", "tone_mapping", value); }
    public bool Deinterlace { get => GetBool("video", "deinterlace", true); set => SetBool("video", "deinterlace", value); }
    public bool Deband { get => GetBool("video", "deband", false); set => SetBool("video", "deband", value); }

    // What to do when playback reaches a marked segment: "ask", "auto" or "none".
    public string IntroAction { get => Get("playback", "intro", "ask"); set => Set("playback", "intro", value); }
    public string CreditsAction { get => Get("playback", "credits", "ask"); set => Set("playback", "credits", value); }
    public string OtherSegmentAction { get => Get("playback", "other_segments", "ask"); set => Set("playback", "other_segments", value); }

    public string SegmentAction(string segmentType) => segmentType switch
    {
        "Intro" => IntroAction,
        "Outro" => CreditsAction,
        _ => OtherSegmentAction,
    };

    /// <summary>mpv audio-channels value: "auto-safe" or "stereo".</summary>
    public string AudioChannels { get => Get("audio", "channels", "auto-safe"); set => Set("audio", "channels", value); }
    /// <summary>Send Dolby and DTS audio to the receiver undecoded.</summary>
    public bool AudioPassthrough { get => GetBool("audio", "passthrough", false); set => SetBool("audio", "passthrough", value); }

    /// <summary>Evens out loud and quiet passages.</summary>
    public bool AudioNightMode { get => GetBool("audio", "night_mode", false); set => SetBool("audio", "night_mode", value); }
    /// <summary>Milliseconds the audio is shifted against the picture. Positive plays it later.</summary>
    public int AudioDelayMs { get => GetInt("audio", "delay_ms", 0); set => SetInt("audio", "delay_ms", value); }

    /// <summary>Milliseconds subtitles are shifted against the picture. Positive shows them later.</summary>
    public int SubtitleDelayMs { get => GetInt("subtitles", "delay_ms", 0); set => SetInt("subtitles", "delay_ms", value); }
    /// <summary>Subtitle text colour as #RRGGBB.</summary>
    public string SubtitleColor { get => Get("subtitles", "color", "#FFFFFF"); set => Set("subtitles", "color", value); }
    /// <summary>Applies the size, colour and position above to subtitles that carry their own styling.</summary>
    public bool SubtitleOverrideStyles { get => GetBool("subtitles", "override_styles", false); set => SetBool("subtitles", "override_styles", value); }

    /// <summary>mpv sub-scale value.</summary>
    public string SubtitleScale { get => Get("subtitles", "scale", "1"); set => Set("subtitles", "scale", value); }
    /// <summary>mpv sub-pos value: 100 is the bottom of the picture.</summary>
    public int SubtitlePosition { get => GetInt("subtitles", "position", 100); set => SetInt("subtitles", "position", value); }
    public bool SubtitleBox { get => GetBool("subtitles", "box", false); set => SetBool("subtitles", "box", value); }

    public bool HomeContinueWatching { get => GetBool("home", "continue_watching", true); set => SetBool("home", "continue_watching", value); }
    public bool HomeNextUp { get => GetBool("home", "next_up", true); set => SetBool("home", "next_up", value); }
    public bool HomeLibraries { get => GetBool("home", "libraries", true); set => SetBool("home", "libraries", value); }
    public bool HomeLatest { get => GetBool("home", "latest", true); set => SetBool("home", "latest", value); }
    public bool NextUpRewatching { get => GetBool("home", "rewatching", false); set => SetBool("home", "rewatching", value); }

    /// <summary>Days a show stays in Next Up after you last watched it. Zero means no limit.</summary>
    public int NextUpDays { get => GetInt("home", "next_up_days", 0); set => SetInt("home", "next_up_days", value); }

    /// <summary>Index into the library sort orders that a library opens with.</summary>
    public int LibrarySort { get => GetInt("library", "sort", 0); set => SetInt("library", "sort", value); }
    public bool CardTitles { get => GetBool("library", "card_titles", true); set => SetBool("library", "card_titles", value); }
    public bool Backdrops { get => GetBool("library", "backdrops", true); set => SetBool("library", "backdrops", value); }

    /// <summary>Top bar clock: "system", "12", "24" or "off".</summary>
    public string Clock { get => Get("ui", "clock", "system"); set => Set("ui", "clock", value); }

    /// <summary>Multiplier on card widths: 0.85 small, 1 medium, 1.15 large.</summary>
    public float CardScale { get => Get("ui", "card_scale", "1").ToFloat(); set => Set("ui", "card_scale", value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }

    public string DeviceId
    {
        get
        {
            var id = Get("device", "id", "");
            if (id == "")
            {
                id = Guid.NewGuid().ToString("N");
                Set("device", "id", id);
            }
            return id;
        }
    }

    public bool HasSession => Server != "" && Token != "" && UserId != "";

    public override void _EnterTree()
    {
        I = this;
        var args = OS.GetCmdlineUserArgs();
        var index = Array.IndexOf(args, "--config");
        if (index >= 0 && index + 1 < args.Length) { Path = args[index + 1]; }
        _file.Load(Path);
    }

    public void ClearSession()
    {
        Token = "";
        UserId = "";
        UserName = "";
    }

    private string Get(string section, string key, string fallback) =>
        _file.GetValue(section, key, fallback).AsString();

    private bool GetBool(string section, string key, bool fallback) => Get(section, key, fallback ? "true" : "false") == "true";
    private void SetBool(string section, string key, bool value) => Set(section, key, value ? "true" : "false");
    private int GetInt(string section, string key, int fallback) => int.TryParse(Get(section, key, ""), out var value) ? value : fallback;
    private void SetInt(string section, string key, int value) => Set(section, key, value.ToString());

    private void Set(string section, string key, string value)
    {
        _file.SetValue(section, key, value);
        _file.Save(Path);
    }
}
