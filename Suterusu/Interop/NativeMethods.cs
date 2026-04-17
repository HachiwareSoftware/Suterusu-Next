using System;
using System.Runtime.InteropServices;

namespace Suterusu.Interop
{
    internal static class NativeMethods
    {
        // Hook types
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        // Mouse messages
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;

        // Process access rights
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // Window messages
        public const int WM_KEYDOWN    = 0x0100;
        public const int WM_KEYUP      = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP   = 0x0105;

        // Flash flags
        public const uint FLASHW_STOP     = 0;
        public const uint FLASHW_CAPTION  = 1;
        public const uint FLASHW_TRAY     = 2;
        public const uint FLASHW_ALL      = 3;
        public const uint FLASHW_TIMER    = 4;
        public const uint FLASHW_TIMERNOFG = 12;

        // Clipboard formats
        public const uint CF_UNICODETEXT = 13;

        // Global memory flags
        public const uint GMEM_MOVEABLE = 0x0002;

        // SetWindowPos
        public const uint SWP_NOSIZE     = 0x0001;
        public const uint SWP_NOMOVE     = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const int  HWND_TOPMOST   = -1;

        // ShowWindow
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;

        // COM initialization flags
        public const uint COINIT_APARTMENTTHREADED = 0x00000002;

        // COM class context / HRESULT values
        public const uint CLSCTX_INPROC_SERVER = 0x1;
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

        // GetWindowLong / SetWindowLong
        public const int GWL_EXSTYLE       = -20;
        public const int WS_EX_TOOLWINDOW  = 0x00000080;
        public const int WS_EX_NOACTIVATE  = 0x08000000;
        public const int WS_EX_TOPMOST     = 0x00000008;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint   vkCode;
            public uint   scanCode;
            public uint   flags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FLASHWINFO
        {
            public uint   cbSize;
            public IntPtr hwnd;
            public uint   dwFlags;
            public uint   uCount;
            public uint   dwTimeout;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>Callback delegate for EnumWindows / EnumChildWindows.</summary>
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        public static extern bool FreeConsole();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        /// <summary>Enumerates all top-level windows on the desktop.</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>Retrieves the process ID that created the specified window.</summary>
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Retrieves the full name of the executable image for the specified process.
        /// Uses the ANSI variant to match the reference C++ implementation.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageNameA(
            IntPtr hProcess,
            uint   dwFlags,
            System.Text.StringBuilder lpExeName,
            ref uint lpdwSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("user32.dll")]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll")]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        public static extern UIntPtr GlobalSize(IntPtr hMem);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Virtual Desktop support
        public static readonly Guid CLSID_VirtualDesktopManager = new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A");
        public static readonly Guid IID_IVirtualDesktopManager = new Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B");

        // COM initialization
        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        public static extern int CoCreateInstance(
            [In] ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }
}

// Virtual Desktop interfaces
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
public interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(IntPtr hWnd, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(IntPtr hWnd, [In] ref Guid desktopId);
}
