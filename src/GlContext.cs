using OpenTK;
using OpenTK.Graphics.OpenGL4;

namespace ScreensaverStargate;

/// <summary>
/// An OpenGL 3.3 core context created with WGL on an existing window handle.
/// Must be created, used and disposed on the same thread.
/// </summary>
internal sealed unsafe class GlContext : IDisposable
{
    const int WGL_CONTEXT_MAJOR_VERSION_ARB = 0x2091;
    const int WGL_CONTEXT_MINOR_VERSION_ARB = 0x2092;
    const int WGL_CONTEXT_PROFILE_MASK_ARB = 0x9126;
    const int WGL_CONTEXT_CORE_PROFILE_BIT_ARB = 0x0001;

    const uint PFD_DOUBLEBUFFER = 0x00000001;
    const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    const uint PFD_SUPPORT_OPENGL = 0x00000020;

    static readonly object BindingsLock = new();
    static bool _bindingsLoaded;
    static IntPtr _opengl32;

    readonly IntPtr _hwnd;
    IntPtr _hdc;
    IntPtr _hglrc;
    delegate* unmanaged<int, int> _swapInterval;
    delegate* unmanaged<int> _getSwapInterval;

    public GlContext(IntPtr hwnd)
    {
        _hwnd = hwnd;
        if (_opengl32 == IntPtr.Zero)
            _opengl32 = Native.LoadLibraryW("opengl32.dll");

        _hdc = Native.GetDC(hwnd);
        if (_hdc == IntPtr.Zero)
            throw new InvalidOperationException("GetDC failed for the render window.");

        var pfd = new Native.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)sizeof(Native.PIXELFORMATDESCRIPTOR),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = 0, // PFD_TYPE_RGBA
            cColorBits = 32,
            cAlphaBits = 8,
        };
        if (Native.GetPixelFormat(_hdc) == 0)
        {
            int format = Native.ChoosePixelFormat(_hdc, ref pfd);
            if (format == 0 || !Native.SetPixelFormat(_hdc, format, ref pfd))
                throw new InvalidOperationException("No suitable OpenGL pixel format is available on this display.");
        }

        // A legacy context is needed to obtain wglCreateContextAttribsARB.
        IntPtr legacy = Native.wglCreateContext(_hdc);
        if (legacy == IntPtr.Zero || !Native.wglMakeCurrent(_hdc, legacy))
            throw new InvalidOperationException("The graphics driver could not create an OpenGL context.");

        var createAttribs = (delegate* unmanaged<IntPtr, IntPtr, int*, IntPtr>)Native.wglGetProcAddress("wglCreateContextAttribsARB");
        if (createAttribs == null)
        {
            Native.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            Native.wglDeleteContext(legacy);
            throw new InvalidOperationException("The graphics driver does not support wglCreateContextAttribsARB (OpenGL 3.3 core is required).");
        }

        int* attribs = stackalloc int[]
        {
            WGL_CONTEXT_MAJOR_VERSION_ARB, 3,
            WGL_CONTEXT_MINOR_VERSION_ARB, 3,
            WGL_CONTEXT_PROFILE_MASK_ARB, WGL_CONTEXT_CORE_PROFILE_BIT_ARB,
            0,
        };
        _hglrc = createAttribs(_hdc, IntPtr.Zero, attribs);
        Native.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        Native.wglDeleteContext(legacy);
        if (_hglrc == IntPtr.Zero)
            throw new InvalidOperationException("The graphics driver does not support OpenGL 3.3 core profile.");

        if (!Native.wglMakeCurrent(_hdc, _hglrc))
            throw new InvalidOperationException("wglMakeCurrent failed for the OpenGL 3.3 context.");

        _swapInterval = (delegate* unmanaged<int, int>)Native.wglGetProcAddress("wglSwapIntervalEXT");
        _getSwapInterval = (delegate* unmanaged<int>)Native.wglGetProcAddress("wglGetSwapIntervalEXT");

        lock (BindingsLock)
        {
            if (!_bindingsLoaded)
            {
                GL.LoadBindings(new WglBindings());
                _bindingsLoaded = true;
            }
        }
    }

    public string Renderer => GL.GetString(StringName.Renderer) ?? "unknown";

    /// <summary>Sets the swap interval and returns the interval the driver reports afterwards (-1 if unknown).</summary>
    public int SetSwapInterval(int interval)
    {
        if (_swapInterval != null)
            _swapInterval(interval);
        return _getSwapInterval != null ? _getSwapInterval() : -1;
    }

    public void SwapBuffers() => Native.SwapBuffers(_hdc);

    public void Dispose()
    {
        if (_hglrc != IntPtr.Zero)
        {
            Native.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            Native.wglDeleteContext(_hglrc);
            _hglrc = IntPtr.Zero;
        }
        if (_hdc != IntPtr.Zero)
        {
            Native.ReleaseDC(_hwnd, _hdc);
            _hdc = IntPtr.Zero;
        }
    }

    sealed class WglBindings : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
        {
            IntPtr p = Native.wglGetProcAddress(procName);
            long v = p.ToInt64();
            if (v is 0 or 1 or 2 or 3 or -1)
                p = Native.GetProcAddress(_opengl32, procName);
            return p;
        }
    }
}
