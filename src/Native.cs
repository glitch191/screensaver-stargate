using System.Runtime.InteropServices;

namespace ScreensaverStargate;

/// <summary>Win32 declarations used by the screensaver.</summary>
internal static unsafe partial class Native
{
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CLIPCHILDREN = 0x02000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const int CS_OWNDC = 0x0020;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;

    public const int WM_DESTROY = 0x0002;
    public const int WM_ERASEBKGND = 0x0014;
    public const int WM_SETCURSOR = 0x0020;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_DPICHANGED = 0x02E0;
    public const int SC_SCREENSAVE = 0xF140;
    public const int SC_MONITORPOWER = 0xF170;

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public const int ENUM_CURRENT_SETTINGS = -1;

    public const int WM_INPUT = 0x00FF;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEMOUSE = 0;
    public const ushort RI_MOUSE_BUTTON_DOWN_MASK = 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0100; // left, right, middle, x1, x2
    public const ushort RI_MOUSE_WHEEL = 0x0400;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    public static extern uint GetRawInputData(IntPtr rawInput, uint command, byte* data, ref uint size, uint headerSize);

    /// <summary>
    /// Registers keyboard and mouse raw input for a window, delivered even when it is not in the
    /// foreground, so the screensaver closes on any key or click whatever window has the focus.
    /// </summary>
    public static bool RegisterInputSink(IntPtr hwnd)
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = hwnd }, // keyboard
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x02, Flags = RIDEV_INPUTSINK, Target = hwnd }, // mouse
        };
        return RegisterRawInputDevices(devices, (uint)devices.Length, (uint)sizeof(RAWINPUTDEVICE));
    }

    /// <summary>True when a WM_INPUT message is a key press, a mouse button press or a wheel turn.</summary>
    public static bool IsRawKeyOrButton(IntPtr lParam, out string description)
    {
        description = "";
        uint headerSize = (uint)sizeof(RAWINPUTHEADER);
        byte* buffer = stackalloc byte[64];
        uint size = 64;
        if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) == unchecked((uint)-1))
            return false;
        var header = (RAWINPUTHEADER*)buffer;
        if (header->Type == RIM_TYPEKEYBOARD)
        {
            // RAWKEYBOARD.Flags bit 0 is RI_KEY_BREAK (key release).
            ushort flags = *(ushort*)(buffer + headerSize + 2);
            ushort vkey = *(ushort*)(buffer + headerSize + 6);
            description = $"raw key 0x{vkey:X2}";
            return (flags & 1) == 0;
        }
        if (header->Type == RIM_TYPEMOUSE)
        {
            // RAWMOUSE: usFlags (2 bytes), padding (2), then usButtonFlags.
            ushort buttons = *(ushort*)(buffer + headerSize + 4);
            description = $"raw mouse buttons 0x{buttons:X4}";
            return (buttons & (RI_MOUSE_BUTTON_DOWN_MASK | RI_MOUSE_WHEEL)) != 0;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte cAlphaBits, cAlphaShift;
        public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers;
        public byte iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DEVMODEW devMode);

    [DllImport("gdi32.dll")]
    public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);

    [DllImport("gdi32.dll")]
    public static extern int GetPixelFormat(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SwapBuffers(IntPtr hdc);

    [DllImport("opengl32.dll")]
    public static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("opengl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern IntPtr wglGetProcAddress(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    public const uint TIMER_ALL_ACCESS = 0x1F0003;

    /// <summary>Returns the current refresh rate of a display device, or 0 if unknown.</summary>
    public static int GetRefreshRate(string deviceName)
    {
        var mode = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
        if (EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS, ref mode) && mode.dmDisplayFrequency > 1)
            return (int)mode.dmDisplayFrequency;
        return 0;
    }
}
