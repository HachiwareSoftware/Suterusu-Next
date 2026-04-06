using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Suterusu.Interop
{
    /// <summary>
    /// Desktop Window Manager (DWM) interop for Windows 11 Mica backdrop effect.
    /// </summary>
    public static class DwmInterop
    {
        // ── DWM P/Invoke ────────────────────────────────────────────────────────

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            DWMWINDOWATTRIBUTE dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        private enum DWMWINDOWATTRIBUTE
        {
            DWMWA_BORDER_COLOR = 34,
            DWMWA_CAPTION_COLOR = 35,
            DWMWA_TEXT_COLOR = 36,
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
            DWMWA_SYSTEMBACKDROP_TYPE = 38
        }

        private enum DWM_SYSTEMBACKDROP_TYPE
        {
            DWMSBT_AUTO = 0,
            DWMSBT_NONE = 1,
            DWMSBT_MAINWINDOW = 2,  // Mica
            DWMSBT_TRANSIENTWINDOW = 3,  // Acrylic
            DWMSBT_TABBEDWINDOW = 4  // Mica Alt
        }

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to enable Windows 11 Mica backdrop on the specified window.
        /// Returns true if successful, false if not supported (Windows 10 or earlier).
        /// </summary>
        public static bool TryEnableMicaBackdrop(Window window)
        {
            if (window == null)
                return false;

            // Check if we're on Windows 11 (build 22000+)
            if (!IsWindows11OrGreater())
                return false;

            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero)
                    return false;

                int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
                int result = DwmSetWindowAttribute(
                    hwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
                    ref backdropType,
                    sizeof(int));

                return result == 0; // S_OK
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to set dark or light mode for the window title bar.
        /// </summary>
        public static bool TrySetDarkMode(Window window, bool useDarkMode)
        {
            if (window == null)
                return false;

            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero)
                    return false;

                int darkModeValue = useDarkMode ? 1 : 0;
                int result = DwmSetWindowAttribute(
                    hwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref darkModeValue,
                    sizeof(int));

                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to force explicit title bar colors regardless of Windows theme.
        /// Colors use Win32 COLORREF format: 0x00BBGGRR.
        /// </summary>
        public static bool TrySetTitleBarColors(Window window, int captionColorRef, int textColorRef, int borderColorRef)
        {
            if (window == null)
                return false;

            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero)
                    return false;

                int captionResult = DwmSetWindowAttribute(
                    hwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR,
                    ref captionColorRef,
                    sizeof(int));

                int textResult = DwmSetWindowAttribute(
                    hwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR,
                    ref textColorRef,
                    sizeof(int));

                int borderResult = DwmSetWindowAttribute(
                    hwnd,
                    DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR,
                    ref borderColorRef,
                    sizeof(int));

                return captionResult == 0 && textResult == 0 && borderResult == 0;
            }
            catch
            {
                return false;
            }
        }

        // ── Windows version detection ───────────────────────────────────────────

        private static bool IsWindows11OrGreater()
        {
            // Windows 11 is version 10.0.22000+
            var version = Environment.OSVersion.Version;
            if (version.Major > 10)
                return true;
            if (version.Major == 10 && version.Build >= 22000)
                return true;
            return false;
        }
    }
}
