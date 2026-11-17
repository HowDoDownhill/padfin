using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Padfin.Player;

/// <summary>
/// Raw OpenGL access to the context Godot's Compatibility renderer owns. mpv draws video with it,
/// so every call here runs on the main thread, where that context is current.
/// </summary>
internal static unsafe class Gl
{
    private const uint Framebuffer = 0x8D40, DrawFramebuffer = 0x8CA9, ReadFramebuffer = 0x8CA8;
    private const uint ColorAttachment0 = 0x8CE0, Texture2D = 0x0DE1, FramebufferComplete = 0x8CD5;
    private const uint ArrayBuffer = 0x8892, PixelUnpackBuffer = 0x88EC;
    private const uint Blend = 0x0BE2, ScissorTest = 0x0C11, DepthTest = 0x0B71, CullFace = 0x0B44, StencilTest = 0x0B90;

    private static IntPtr _glLibrary;
    private static delegate* unmanaged<IntPtr, IntPtr> _platformGetProc;

    private static delegate* unmanaged<int, uint*, void> _genFramebuffers;
    private static delegate* unmanaged<int, uint*, void> _deleteFramebuffers;
    private static delegate* unmanaged<uint, uint, void> _bindFramebuffer;
    private static delegate* unmanaged<uint, uint, uint, uint, int, void> _framebufferTexture2D;
    private static delegate* unmanaged<uint, uint> _checkFramebufferStatus;
    private static delegate* unmanaged<uint, int*, void> _getIntegerv;
    private static delegate* unmanaged<uint, byte*, void> _getBooleanv;
    private static delegate* unmanaged<uint, byte> _isEnabled;
    private static delegate* unmanaged<uint, void> _enable;
    private static delegate* unmanaged<uint, void> _disable;
    private static delegate* unmanaged<int, int, int, int, void> _viewport;
    private static delegate* unmanaged<int, int, int, int, void> _scissor;
    private static delegate* unmanaged<uint, void> _useProgram;
    private static delegate* unmanaged<uint, void> _activeTexture;
    private static delegate* unmanaged<uint, uint, void> _bindTexture;
    private static delegate* unmanaged<uint, void> _bindVertexArray;
    private static delegate* unmanaged<uint, uint, void> _bindBuffer;
    private static delegate* unmanaged<uint, int, void> _pixelStorei;
    private static delegate* unmanaged<uint, uint, uint, uint, void> _blendFuncSeparate;
    private static delegate* unmanaged<uint, uint, void> _blendEquationSeparate;
    private static delegate* unmanaged<byte, byte, byte, byte, void> _colorMask;
    private static delegate* unmanaged<byte, void> _depthMask;

    public static bool Ready { get; private set; }

    [DllImport("opengl32")] private static extern IntPtr wglGetCurrentContext();

    /// <summary>Binds to the current GL context. False when Godot is not running on a desktop GL or EGL context we can reach.</summary>
    public static bool Init()
    {
        if (Ready) { return true; }
        try
        {
            if (!FindContext()) { return false; }
            _genFramebuffers = (delegate* unmanaged<int, uint*, void>)Proc("glGenFramebuffers");
            _deleteFramebuffers = (delegate* unmanaged<int, uint*, void>)Proc("glDeleteFramebuffers");
            _bindFramebuffer = (delegate* unmanaged<uint, uint, void>)Proc("glBindFramebuffer");
            _framebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)Proc("glFramebufferTexture2D");
            _checkFramebufferStatus = (delegate* unmanaged<uint, uint>)Proc("glCheckFramebufferStatus");
            _getIntegerv = (delegate* unmanaged<uint, int*, void>)Proc("glGetIntegerv");
            _getBooleanv = (delegate* unmanaged<uint, byte*, void>)Proc("glGetBooleanv");
            _isEnabled = (delegate* unmanaged<uint, byte>)Proc("glIsEnabled");
            _enable = (delegate* unmanaged<uint, void>)Proc("glEnable");
            _disable = (delegate* unmanaged<uint, void>)Proc("glDisable");
            _viewport = (delegate* unmanaged<int, int, int, int, void>)Proc("glViewport");
            _scissor = (delegate* unmanaged<int, int, int, int, void>)Proc("glScissor");
            _useProgram = (delegate* unmanaged<uint, void>)Proc("glUseProgram");
            _activeTexture = (delegate* unmanaged<uint, void>)Proc("glActiveTexture");
            _bindTexture = (delegate* unmanaged<uint, uint, void>)Proc("glBindTexture");
            _bindVertexArray = (delegate* unmanaged<uint, void>)Proc("glBindVertexArray");
            _bindBuffer = (delegate* unmanaged<uint, uint, void>)Proc("glBindBuffer");
            _pixelStorei = (delegate* unmanaged<uint, int, void>)Proc("glPixelStorei");
            _blendFuncSeparate = (delegate* unmanaged<uint, uint, uint, uint, void>)Proc("glBlendFuncSeparate");
            _blendEquationSeparate = (delegate* unmanaged<uint, uint, void>)Proc("glBlendEquationSeparate");
            _colorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)Proc("glColorMask");
            _depthMask = (delegate* unmanaged<byte, void>)Proc("glDepthMask");
            Ready = true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            Godot.GD.Print("OpenGL interop unavailable: ", e.Message);
            Ready = false;
        }
        return Ready;
    }

    private static bool FindContext()
    {
        if (OperatingSystem.IsWindows())
        {
            // ANGLE builds of the renderer have no WGL context. Those fall back to software rendering.
            if (wglGetCurrentContext() == IntPtr.Zero) { return false; }
            _glLibrary = NativeLibrary.Load("opengl32");
            _platformGetProc = (delegate* unmanaged<IntPtr, IntPtr>)NativeLibrary.GetExport(_glLibrary, "wglGetProcAddress");
            return true;
        }

        if (NativeLibrary.TryLoad("libEGL.so.1", out var egl)
            && NativeLibrary.TryGetExport(egl, "eglGetCurrentContext", out var eglCurrent)
            && ((delegate* unmanaged<IntPtr>)eglCurrent)() != IntPtr.Zero)
        {
            _platformGetProc = (delegate* unmanaged<IntPtr, IntPtr>)NativeLibrary.GetExport(egl, "eglGetProcAddress");
            NativeLibrary.TryLoad("libGL.so.1", out _glLibrary);
            return true;
        }
        if (NativeLibrary.TryLoad("libGL.so.1", out var glx)
            && NativeLibrary.TryGetExport(glx, "glXGetCurrentContext", out var glxCurrent)
            && ((delegate* unmanaged<IntPtr>)glxCurrent)() != IntPtr.Zero)
        {
            _platformGetProc = (delegate* unmanaged<IntPtr, IntPtr>)NativeLibrary.GetExport(glx, "glXGetProcAddressARB");
            _glLibrary = glx;
            return true;
        }
        return false;
    }

    /// <summary>Address of a GL function, or zero.</summary>
    public static IntPtr Lookup(IntPtr name)
    {
        var address = _platformGetProc != null ? _platformGetProc(name) : IntPtr.Zero;
        // wglGetProcAddress answers small sentinel values for core 1.1 functions, which live in opengl32 itself.
        var value = (long)address;
        if (value is >= -1 and <= 3)
        {
            address = IntPtr.Zero;
            if (_glLibrary != IntPtr.Zero) { NativeLibrary.TryGetExport(_glLibrary, Marshal.PtrToStringUTF8(name), out address); }
        }
        return address;
    }

    private static IntPtr Proc(string name)
    {
        var text = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            var address = Lookup(text);
            if (address == IntPtr.Zero) { throw new EntryPointNotFoundException(name); }
            return address;
        }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    /// <summary>The resolver handed to mpv.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr MpvGetProcAddress(IntPtr context, IntPtr name) => Lookup(name);

    public static uint CreateFramebuffer()
    {
        uint fbo;
        _genFramebuffers(1, &fbo);
        return fbo;
    }

    public static void DeleteFramebuffer(uint fbo)
    {
        if (fbo != 0) { _deleteFramebuffers(1, &fbo); }
    }

    /// <summary>Points the framebuffer's colour output at a texture. False when the pair is unusable.</summary>
    public static bool AttachTexture(uint fbo, uint texture)
    {
        int previous;
        _getIntegerv(0x8CA6, &previous);
        _bindFramebuffer(Framebuffer, fbo);
        _framebufferTexture2D(Framebuffer, ColorAttachment0, Texture2D, texture, 0);
        var complete = _checkFramebufferStatus(Framebuffer) == FramebufferComplete;
        _bindFramebuffer(Framebuffer, (uint)previous);
        return complete;
    }

    /// <summary>The GL state Godot's renderer tracks and mpv changes while drawing.</summary>
    public struct State
    {
        public int DrawFbo, ReadFbo, Program, ActiveTexture, Texture, VertexArray, ArrayBuffer, UnpackBuffer, UnpackAlignment;
        public int BlendSrcRgb, BlendDstRgb, BlendSrcAlpha, BlendDstAlpha, BlendEqRgb, BlendEqAlpha;
        public fixed int Viewport[4];
        public fixed int Scissor[4];
        public fixed byte ColorMask[4];
        public byte DepthMask, Blend, ScissorTest, DepthTest, CullFace, StencilTest;
    }

    public static State Save()
    {
        State s;
        _getIntegerv(0x8CA6, &s.DrawFbo);
        _getIntegerv(0x8CAA, &s.ReadFbo);
        _getIntegerv(0x8B8D, &s.Program);
        _getIntegerv(0x84E0, &s.ActiveTexture);
        _getIntegerv(0x8069, &s.Texture);
        _getIntegerv(0x85B5, &s.VertexArray);
        _getIntegerv(0x8894, &s.ArrayBuffer);
        _getIntegerv(0x88EF, &s.UnpackBuffer);
        _getIntegerv(0x0CF5, &s.UnpackAlignment);
        _getIntegerv(0x80C9, &s.BlendSrcRgb);
        _getIntegerv(0x80C8, &s.BlendDstRgb);
        _getIntegerv(0x80CB, &s.BlendSrcAlpha);
        _getIntegerv(0x80CA, &s.BlendDstAlpha);
        _getIntegerv(0x8009, &s.BlendEqRgb);
        _getIntegerv(0x883D, &s.BlendEqAlpha);
        _getIntegerv(0x0BA2, s.Viewport);
        _getIntegerv(0x0C10, s.Scissor);
        _getBooleanv(0x0C23, s.ColorMask);
        _getBooleanv(0x0B72, &s.DepthMask);
        s.Blend = _isEnabled(Blend);
        s.ScissorTest = _isEnabled(ScissorTest);
        s.DepthTest = _isEnabled(DepthTest);
        s.CullFace = _isEnabled(CullFace);
        s.StencilTest = _isEnabled(StencilTest);
        return s;
    }

    public static void Restore(State s)
    {
        _bindFramebuffer(DrawFramebuffer, (uint)s.DrawFbo);
        _bindFramebuffer(ReadFramebuffer, (uint)s.ReadFbo);
        _useProgram((uint)s.Program);
        _bindVertexArray((uint)s.VertexArray);
        _bindBuffer(ArrayBuffer, (uint)s.ArrayBuffer);
        _bindBuffer(PixelUnpackBuffer, (uint)s.UnpackBuffer);
        _pixelStorei(0x0CF5, s.UnpackAlignment);
        _activeTexture((uint)s.ActiveTexture);
        _bindTexture(Texture2D, (uint)s.Texture);
        _blendFuncSeparate((uint)s.BlendSrcRgb, (uint)s.BlendDstRgb, (uint)s.BlendSrcAlpha, (uint)s.BlendDstAlpha);
        _blendEquationSeparate((uint)s.BlendEqRgb, (uint)s.BlendEqAlpha);
        _viewport(s.Viewport[0], s.Viewport[1], s.Viewport[2], s.Viewport[3]);
        _scissor(s.Scissor[0], s.Scissor[1], s.Scissor[2], s.Scissor[3]);
        _colorMask(s.ColorMask[0], s.ColorMask[1], s.ColorMask[2], s.ColorMask[3]);
        _depthMask(s.DepthMask);
        Toggle(Blend, s.Blend);
        Toggle(ScissorTest, s.ScissorTest);
        Toggle(DepthTest, s.DepthTest);
        Toggle(CullFace, s.CullFace);
        Toggle(StencilTest, s.StencilTest);
    }

    private static void Toggle(uint capability, byte enabled)
    {
        if (enabled != 0) { _enable(capability); } else { _disable(capability); }
    }
}
