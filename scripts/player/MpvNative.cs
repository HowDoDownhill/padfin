using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;

namespace Padfin.Player;

/// <summary>P/Invoke surface for libmpv's client and render APIs.</summary>
internal static unsafe class MpvNative
{
    private const string Lib = "libmpv";

    public const int FormatString = 1;
    public const int FormatFlag = 3;
    public const int FormatDouble = 5;

    public const int EventNone = 0;
    public const int EventShutdown = 1;
    public const int EventEndFileId = 7;
    public const int EventFileLoadedId = 8;

    public const int EndReasonEof = 0;
    public const int EndReasonError = 4;

    public const int RenderParamApiType = 1;
    public const int RenderParamOpenGlInitParams = 2;
    public const int RenderParamOpenGlFbo = 3;
    public const int RenderParamFlipY = 4;
    public const int RenderParamBlockForTargetTime = 12;
    public const int RenderParamSwSize = 17;
    public const int RenderParamSwFormat = 18;
    public const int RenderParamSwStride = 19;
    public const int RenderParamSwPointer = 20;

    public const ulong RenderUpdateFrame = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public int Id;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventEndFile
    {
        public int Reason;
        public int Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RenderParam
    {
        public int Type;
        public void* Data;
        public RenderParam(int type, void* data) { Type = type; Data = data; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpenGlInitParams
    {
        public delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr> GetProcAddress;
        public IntPtr Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpenGlFbo
    {
        public int Fbo;
        public int Width;
        public int Height;
        public int InternalFormat;
    }

    static MpvNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly, Resolve);
    }

    /// <summary>Forces the static constructor so the resolver is installed before the first call.</summary>
    public static void EnsureLoaded() { }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib) { return IntPtr.Zero; }
        var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        string[] candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(exeDir, "libmpv-2.dll"),
                Path.Combine(exeDir, "native", "win-x64", "libmpv-2.dll"),
                ProjectSettings.GlobalizePath("res://native/win-x64/libmpv-2.dll"),
                "libmpv-2.dll",
            }
            : new[]
            {
                Path.Combine(exeDir, "libmpv.so.2"),
                "libmpv.so.2",
                "libmpv.so",
            };
        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle)) { return handle; }
        }
        return IntPtr.Zero;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_create();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_free(IntPtr data);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_error_string(int error);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern Event* mpv_wait_event(IntPtr ctx, double timeout);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_command(IntPtr ctx, IntPtr* args);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_get_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, void* data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_render_context_create(out IntPtr renderContext, IntPtr mpv, RenderParam* parameters);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_render_context_set_update_callback(IntPtr renderContext, delegate* unmanaged[Cdecl]<IntPtr, void> callback, IntPtr context);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ulong mpv_render_context_update(IntPtr renderContext);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_render_context_render(IntPtr renderContext, RenderParam* parameters);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_render_context_free(IntPtr renderContext);

    public static string ErrorText(int code) => Marshal.PtrToStringUTF8(mpv_error_string(code)) ?? code.ToString();
}
