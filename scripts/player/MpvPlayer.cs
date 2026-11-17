using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace Padfin.Player;

public class MpvTrack
{
    public int Id;
    public string Type;
    public string Language;
    public string Title;
    public string Codec;
    public int Channels;
    public bool Selected;
    public bool External;

    public string Label
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Language)) { parts.Add(LanguageName(Language)); }
            if (!string.IsNullOrEmpty(Title)) { parts.Add(Title); }
            if (!string.IsNullOrEmpty(Codec)) { parts.Add(Codec.ToUpperInvariant()); }
            if (Type == "audio" && Channels > 0) { parts.Add(Channels switch { 1 => "Mono", 2 => "Stereo", 6 => "5.1", 8 => "7.1", _ => Channels + " ch" }); }
            if (External) { parts.Add("External"); }
            return parts.Count == 0 ? $"Track {Id}" : string.Join(" · ", parts);
        }
    }

    private static string LanguageName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code.Length == 3 ? ThreeToTwo(code) : code);
            return culture.EnglishName.Contains("Unknown") ? code : culture.EnglishName;
        }
        catch (CultureNotFoundException) { return code; }
    }

    private static string ThreeToTwo(string code) => code.ToLowerInvariant() switch
    {
        "eng" => "en", "spa" => "es", "fre" or "fra" => "fr", "ger" or "deu" => "de", "ita" => "it", "jpn" => "ja",
        "kor" => "ko", "chi" or "zho" => "zh", "por" => "pt", "rus" => "ru", "dut" or "nld" => "nl", "swe" => "sv",
        "nor" => "no", "dan" => "da", "fin" => "fi", "pol" => "pl", "ara" => "ar", "hin" => "hi", "tur" => "tr",
        _ => code,
    };
}

/// <summary>
/// libmpv playing into a Godot texture. With the Compatibility renderer mpv draws straight into the
/// texture on the GPU. On other renderers it falls back to mpv's software renderer.
/// Everything here runs on the main thread.
/// </summary>
public sealed unsafe class MpvPlayer : IDisposable
{
    private static int _updatePending;

    private IntPtr _mpv;
    private IntPtr _render;
    private bool _useGl;
    private uint _fbo;
    private int _width;
    private int _height;
    private byte[] _swBuffer;
    private Image _swImage;

    public ImageTexture Texture { get; private set; }
    public bool IsGpuRendered => _useGl;

    public event Action FileLoaded;
    /// <summary>Playback of the file ended. The argument is true when it ended because of an error.</summary>
    public event Action<bool> Ended;

    public MpvPlayer()
    {
        MpvNative.EnsureLoaded();
        try { _mpv = MpvNative.mpv_create(); }
        catch (DllNotFoundException)
        {
            throw new InvalidOperationException("libmpv was not found. On Windows run tools/fetch-mpv.ps1. On Linux put libmpv.so.2 beside the executable or install libmpv2.");
        }
        if (_mpv == IntPtr.Zero) { throw new InvalidOperationException("mpv could not start."); }

        Option("config", "no");
        Option("terminal", "no");
        Option("vo", "libmpv");
        Option("osc", "no");
        Option("osd-level", "0");
        Option("input-default-bindings", "no");
        Option("input-vo-keyboard", "no");
        Option("ytdl", "no");
        Option("idle", "yes");
        Option("keep-open", "no");
        // On Linux, mpv can hand VAAPI frames straight to OpenGL only when it owns the window.
        // Drawing into Godot's context, the copying decoders are the ones that work on Intel and AMD.
        var hwdec = Config.I.HardwareDecoding;
        if (OperatingSystem.IsLinux() && hwdec == "auto-safe") { hwdec = "auto-copy-safe"; }
        Option("hwdec", hwdec);
        Option("video-timing-offset", "0");
        Option("audio-client-name", Jellyfin.ClientName);
        Option("user-agent", $"{Jellyfin.ClientName}/{Jellyfin.ClientVersion}");
        Option("cache", "yes");
        // Read ahead as far as the buffer setting allows. A quarter as much again is kept behind the
        // playback position, so a short skip back does not have to download again.
        var buffer = Math.Max(16, Config.I.BufferMegabytes);
        Option("demuxer-max-bytes", $"{buffer}MiB");
        Option("demuxer-max-back-bytes", $"{buffer / 4}MiB");
        Option("sub-auto", "no");
        Option("volume-max", "100");
        var config = Config.I;
        if (config.VideoScaling != "default") { Option("profile", config.VideoScaling); }
        Option("tone-mapping", config.ToneMapping);
        Option("deinterlace", config.Deinterlace ? "auto" : "no");
        Option("deband", config.Deband ? "yes" : "no");
        if (config.VideoZoom == "fill") { Option("panscan", "1.0"); }
        else if (config.VideoZoom == "stretch") { Option("keepaspect", "no"); }
        if (config.AudioNightMode) { Option("af", "dynaudnorm"); }
        Option("audio-delay", (config.AudioDelayMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        Option("sub-delay", (config.SubtitleDelayMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        Option("sub-color", config.SubtitleColor);
        if (config.SubtitleOverrideStyles) { Option("sub-ass-override", "force"); }
        Option("audio-channels", Config.I.AudioChannels);
        if (Config.I.AudioPassthrough) { Option("audio-spdif", "ac3,eac3,dts,dts-hd,truehd"); }
        Option("sub-scale", Config.I.SubtitleScale);
        Option("sub-pos", Config.I.SubtitlePosition.ToString());
        if (Config.I.SubtitleBox)
        {
            Option("sub-border-style", "opaque-box");
            Option("sub-back-color", "#B0000000");
        }

        Check(MpvNative.mpv_initialize(_mpv), "initialize");
        CreateRenderContext();
    }

    private void Option(string name, string value)
    {
        var result = MpvNative.mpv_set_option_string(_mpv, name, value);
        if (result < 0) { GD.Print($"mpv option {name}={value}: {MpvNative.ErrorText(result)}"); }
    }

    private static void Check(int result, string what)
    {
        if (result < 0) { throw new InvalidOperationException($"mpv {what} failed: {MpvNative.ErrorText(result)}"); }
    }

    private void CreateRenderContext()
    {
        var driver = RenderingServer.GetCurrentRenderingDriverName();
        if (driver.StartsWith("opengl3") && Gl.Init())
        {
            var api = Marshal.StringToCoTaskMemUTF8("opengl");
            var init = new MpvNative.OpenGlInitParams { GetProcAddress = &Gl.MpvGetProcAddress };
            var parameters = stackalloc MpvNative.RenderParam[]
            {
                new(MpvNative.RenderParamApiType, (void*)api),
                new(MpvNative.RenderParamOpenGlInitParams, &init),
                new(0, null),
            };
            var state = Gl.Save();
            var result = MpvNative.mpv_render_context_create(out _render, _mpv, parameters);
            Gl.Restore(state);
            Marshal.FreeCoTaskMem(api);
            if (result >= 0)
            {
                _useGl = true;
                _fbo = Gl.CreateFramebuffer();
            }
            else
            {
                GD.Print("mpv OpenGL renderer unavailable: ", MpvNative.ErrorText(result));
                _render = IntPtr.Zero;
            }
        }

        if (!_useGl)
        {
            var api = Marshal.StringToCoTaskMemUTF8("sw");
            var parameters = stackalloc MpvNative.RenderParam[]
            {
                new(MpvNative.RenderParamApiType, (void*)api),
                new(0, null),
            };
            var result = MpvNative.mpv_render_context_create(out _render, _mpv, parameters);
            Marshal.FreeCoTaskMem(api);
            Check(result, "render context");
            GD.Print("mpv is using the software renderer.");
        }

        Interlocked.Exchange(ref _updatePending, 0);
        MpvNative.mpv_render_context_set_update_callback(_render, &OnRenderUpdate, IntPtr.Zero);
    }

    /// <summary>mpv calls this from its own threads when a new frame is ready.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnRenderUpdate(IntPtr context) => Interlocked.Exchange(ref _updatePending, 1);

    // ---- Control ----

    /// <param name="audio">The audio track to start with: a track number, or "auto".</param>
    /// <param name="subtitle">The subtitle track to start with: a track number, "no", or "auto".</param>
    public void Load(string url, double startSeconds, string httpHeader, string audio = "auto", string subtitle = "auto")
    {
        // Chosen before the file opens. Switching tracks after it has started makes mpv seek again.
        SetProperty("aid", audio);
        SetProperty("sid", subtitle);
        SetProperty("http-header-fields", httpHeader ?? "");
        SetProperty("start", startSeconds > 1 ? startSeconds.ToString("0.###", CultureInfo.InvariantCulture) : "none");
        SetProperty("pause", "no");
        Command("loadfile", url, "replace");
    }

    public void Stop() => Command("stop");

    public void Command(params string[] args)
    {
        if (_mpv == IntPtr.Zero) { return; }
        var pointers = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++) { pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]); }
            fixed (IntPtr* p = pointers)
            {
                var result = MpvNative.mpv_command(_mpv, p);
                if (result < 0) { GD.Print($"mpv {string.Join(' ', args)}: {MpvNative.ErrorText(result)}"); }
            }
        }
        finally
        {
            foreach (var pointer in pointers) { if (pointer != IntPtr.Zero) { Marshal.FreeCoTaskMem(pointer); } }
        }
    }

    public void SetProperty(string name, string value)
    {
        if (_mpv != IntPtr.Zero) { MpvNative.mpv_set_property_string(_mpv, name, value); }
    }

    public string GetString(string name)
    {
        if (_mpv == IntPtr.Zero) { return null; }
        var pointer = MpvNative.mpv_get_property_string(_mpv, name);
        if (pointer == IntPtr.Zero) { return null; }
        var text = Marshal.PtrToStringUTF8(pointer);
        MpvNative.mpv_free(pointer);
        return text;
    }

    public double GetDouble(string name)
    {
        double value = 0;
        if (_mpv == IntPtr.Zero || MpvNative.mpv_get_property(_mpv, name, MpvNative.FormatDouble, &value) < 0) { return 0; }
        return value;
    }

    public bool GetFlag(string name)
    {
        var value = 0;
        if (_mpv == IntPtr.Zero || MpvNative.mpv_get_property(_mpv, name, MpvNative.FormatFlag, &value) < 0) { return false; }
        return value != 0;
    }

    public double Position => GetDouble("time-pos");
    public double Duration => GetDouble("duration");
    public bool Buffering => GetFlag("paused-for-cache") || GetFlag("seeking");

    public bool Paused
    {
        get => GetFlag("pause");
        set => SetProperty("pause", value ? "yes" : "no");
    }

    public double Speed
    {
        get => GetDouble("speed");
        set => SetProperty("speed", value.ToString("0.##", CultureInfo.InvariantCulture));
    }

    public void SeekRelative(double seconds) => Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "relative");
    public void SeekTo(double seconds) => Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute");

    public List<MpvTrack> Tracks(string type)
    {
        var tracks = new List<MpvTrack>();
        var count = (int)GetDouble("track-list/count");
        for (var i = 0; i < count; i++)
        {
            var prefix = $"track-list/{i}/";
            if (GetString(prefix + "type") != type) { continue; }
            tracks.Add(new MpvTrack
            {
                Id = (int)GetDouble(prefix + "id"),
                Type = type,
                Language = GetString(prefix + "lang"),
                Title = GetString(prefix + "title"),
                Codec = GetString(prefix + "codec"),
                Channels = (int)GetDouble(prefix + "demux-channel-count"),
                Selected = GetFlag(prefix + "selected"),
                External = GetFlag(prefix + "external"),
            });
        }
        return tracks;
    }

    public void SelectAudio(int id) => SetProperty("aid", id.ToString());
    public void SelectSubtitle(int? id) => SetProperty("sid", id?.ToString() ?? "no");

    /// <param name="select">Shows this subtitle straight away.</param>
    public void AddSubtitle(string url, string title, string language, bool select = false) =>
        Command("sub-add", url, select ? "select" : "auto", title ?? "", language ?? "");

    // ---- Frame loop ----

    /// <summary>Call once per frame with the output size in pixels.</summary>
    public void Update(Vector2I size)
    {
        if (_mpv == IntPtr.Zero) { return; }
        PumpEvents();
        if (_mpv == IntPtr.Zero || _render == IntPtr.Zero) { return; }

        size = new Vector2I(Math.Max(16, size.X), Math.Max(16, size.Y));
        var resized = size.X != _width || size.Y != _height || Texture == null;
        if (resized) { Resize(size); }

        var pending = Interlocked.Exchange(ref _updatePending, 0) == 1;
        if (!pending && !resized) { return; }
        var flags = MpvNative.mpv_render_context_update(_render);
        if ((flags & MpvNative.RenderUpdateFrame) == 0 && !resized) { return; }

        if (_useGl) { RenderGl(); } else { RenderSoftware(); }
    }

    private void Resize(Vector2I size)
    {
        _width = size.X;
        _height = size.Y;
        if (_useGl)
        {
            var image = Image.CreateEmpty(_width, _height, false, Image.Format.Rgba8);
            image.Fill(Colors.Black);
            Texture = ImageTexture.CreateFromImage(image);
            var handle = (uint)RenderingServer.TextureGetNativeHandle(Texture.GetRid());
            if (!Gl.AttachTexture(_fbo, handle)) { GD.PrintErr("mpv framebuffer is incomplete."); }
        }
        else
        {
            _swBuffer = new byte[_width * _height * 3];
            _swImage = Image.CreateFromData(_width, _height, false, Image.Format.Rgb8, _swBuffer);
            Texture = ImageTexture.CreateFromImage(_swImage);
        }
    }

    private void RenderGl()
    {
        var fbo = new MpvNative.OpenGlFbo { Fbo = (int)_fbo, Width = _width, Height = _height };
        // Unflipped output lands the right way up in a Godot texture.
        var flip = 0;
        var block = 0;
        var parameters = stackalloc MpvNative.RenderParam[]
        {
            new(MpvNative.RenderParamOpenGlFbo, &fbo),
            new(MpvNative.RenderParamFlipY, &flip),
            new(MpvNative.RenderParamBlockForTargetTime, &block),
            new(0, null),
        };
        var state = Gl.Save();
        MpvNative.mpv_render_context_render(_render, parameters);
        Gl.Restore(state);
    }

    private void RenderSoftware()
    {
        var size = stackalloc int[] { _width, _height };
        var stride = (nuint)(_width * 3);
        var format = Marshal.StringToCoTaskMemUTF8("rgb24");
        fixed (byte* pixels = _swBuffer)
        {
            var parameters = stackalloc MpvNative.RenderParam[]
            {
                new(MpvNative.RenderParamSwSize, size),
                new(MpvNative.RenderParamSwFormat, (void*)format),
                new(MpvNative.RenderParamSwStride, &stride),
                new(MpvNative.RenderParamSwPointer, pixels),
                new(0, null),
            };
            MpvNative.mpv_render_context_render(_render, parameters);
        }
        Marshal.FreeCoTaskMem(format);
        _swImage.SetData(_width, _height, false, Image.Format.Rgb8, _swBuffer);
        Texture.Update(_swImage);
    }

    private void PumpEvents()
    {
        while (_mpv != IntPtr.Zero)
        {
            var e = MpvNative.mpv_wait_event(_mpv, 0);
            if (e == null || e->Id == MpvNative.EventNone) { return; }
            switch (e->Id)
            {
                case MpvNative.EventFileLoadedId:
                    FileLoaded?.Invoke();
                    break;
                case MpvNative.EventEndFileId:
                    var reason = ((MpvNative.EventEndFile*)e->Data)->Reason;
                    if (reason == MpvNative.EndReasonEof) { Ended?.Invoke(false); }
                    else if (reason == MpvNative.EndReasonError) { Ended?.Invoke(true); }
                    break;
                case MpvNative.EventShutdown:
                    return;
            }
        }
    }

    public void Dispose()
    {
        if (_render != IntPtr.Zero)
        {
            MpvNative.mpv_render_context_set_update_callback(_render, null, IntPtr.Zero);
            if (_useGl)
            {
                var state = Gl.Save();
                MpvNative.mpv_render_context_free(_render);
                Gl.Restore(state);
                Gl.DeleteFramebuffer(_fbo);
            }
            else { MpvNative.mpv_render_context_free(_render); }
            _render = IntPtr.Zero;
        }
        if (_mpv != IntPtr.Zero)
        {
            MpvNative.mpv_terminate_destroy(_mpv);
            _mpv = IntPtr.Zero;
        }
        Texture = null;
    }
}
