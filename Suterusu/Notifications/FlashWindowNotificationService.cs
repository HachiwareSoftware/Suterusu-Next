using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Suterusu.Interop;
using Suterusu.Services;

namespace Suterusu.Notifications
{
    /// <summary>
    /// Flashes the taskbar button of the configured target process to signal completion.
    /// Ports the EnumWindows-based FlashWindow logic from the original C++ implementation.
    /// </summary>
    public class FlashWindowNotificationService : INotificationService
    {
        private readonly ILogger _logger = new NLogLogger("Suterusu.Notification.Flash");
        private readonly string  _target;
        private readonly int     _durationMs;

        /// <param name="flashWindowTarget">
        /// Process name (or partial) to match, e.g. "Chrome". 
        /// Use "All" to flash every visible window, or "None" / empty to disable.
        /// </param>
        /// <param name="flashWindowDurationMs">
        /// Milliseconds to hold the flash before sending FLASHW_STOP (default 1600).
        /// </param>
        public FlashWindowNotificationService(string flashWindowTarget, int flashWindowDurationMs)
        {
            _target     = string.IsNullOrWhiteSpace(flashWindowTarget) ? "Chrome" : flashWindowTarget;
            _durationMs = flashWindowDurationMs > 0 ? flashWindowDurationMs : 1600;
        }

        public void NotifySuccess() => FlashConfiguredWindow(successCount: 3);
        public void NotifyFailure() => FlashConfiguredWindow(successCount: 5);

        // ─────────────────────────────────────────────────────────────
        //  Window enumeration
        // ─────────────────────────────────────────────────────────────

        private void FlashConfiguredWindow(uint successCount)
        {
            string normalizedTarget = _target.Trim().ToLowerInvariant();
            _logger.Debug($"FlashWindow: searching for '{_target}' windows...");

            try
            {
                NativeMethods.EnumWindows((hwnd, _lParam) =>
                {
                    // Skip invisible windows early (matches reference: if (!IsWindowVisible(hwnd)) return TRUE)
                    if (!NativeMethods.IsWindowVisible(hwnd))
                        return true;

                    string exeName = GetProcessExeName(hwnd);
                    if (exeName == null)
                        return true;

                    if (!ShouldFlash(exeName, normalizedTarget))
                        return true;

                    _logger.Debug($"FlashWindow: matched '{exeName}', flashing...");
                    FlashHwnd(hwnd, successCount);

                    // Return false to stop enumeration after the first match
                    // (matches reference behaviour: EnumWindowsCallback returns FALSE on match)
                    return normalizedTarget == "all";

                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                _logger.Error("FlashConfiguredWindow failed.", ex);
            }
        }

        /// <summary>
        /// Resolves the lower-case executable filename (without path) for the process
        /// that owns <paramref name="hwnd"/>, or <c>null</c> if it cannot be determined.
        /// Mirrors the C++ EnumWindowsCallback logic exactly.
        /// </summary>
        private string GetProcessExeName(IntPtr hwnd)
        {
            uint processId = 0;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0)
                return null;

            IntPtr hProcess = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

            if (hProcess == IntPtr.Zero)
                return null;

            try
            {
                var sb   = new StringBuilder(260); // MAX_PATH
                uint size = (uint)sb.Capacity;
                if (!NativeMethods.QueryFullProcessImageNameA(hProcess, 0, sb, ref size))
                    return null;

                string fullPath = sb.ToString();
                int lastSlash   = fullPath.LastIndexOfAny(new[] { '\\', '/' });
                string exeName  = lastSlash >= 0 ? fullPath.Substring(lastSlash + 1) : fullPath;
                return exeName.ToLowerInvariant();
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }

        /// <summary>
        /// Determines whether <paramref name="exeName"/> (lower-case, with .exe) matches
        /// the lower-case <paramref name="target"/> string.
        /// Supports "all", "none"/"", exact match, and partial/substring match.
        /// </summary>
        private static bool ShouldFlash(string exeName, string target)
        {
            if (target == "none" || target == string.Empty)
                return false;

            if (target == "all")
                return true;

            // Normalise target: ensure it carries ".exe" for comparison
            string targetWithExt = target.EndsWith(".exe") ? target : target + ".exe";

            // Exact match (with or without extension) or substring match
            return exeName == targetWithExt
                || exeName == target
                || exeName.Contains(target);
        }

        // ─────────────────────────────────────────────────────────────
        //  Flash primitives
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Flashes <paramref name="hwnd"/> <paramref name="count"/> times using
        /// FLASHW_TRAY only (taskbar button, not the caption bar), then stops after
        /// <see cref="_durationMs"/> milliseconds — matching the reference C++ behaviour.
        /// </summary>
        private void FlashHwnd(IntPtr hwnd, uint count)
        {
            try
            {
                // Start flash: FLASHW_TRAY with a fixed count, no FLASHW_TIMERNOFG so
                // uCount is honoured and the window does not need to leave foreground.
                var info = new NativeMethods.FLASHWINFO
                {
                    cbSize    = (uint)Marshal.SizeOf(typeof(NativeMethods.FLASHWINFO)),
                    hwnd      = hwnd,
                    dwFlags   = NativeMethods.FLASHW_TRAY,
                    uCount    = count,
                    dwTimeout = 0
                };
                NativeMethods.FlashWindowEx(ref info);

                // Hold the flash for the configured duration on this (background) thread,
                // then explicitly stop it — mirroring the C++ Sleep(1600) + FLASHW_STOP sequence.
                Thread.Sleep(_durationMs);

                info.dwFlags = NativeMethods.FLASHW_STOP;
                info.uCount  = 0;
                NativeMethods.FlashWindowEx(ref info);
            }
            catch (Exception ex)
            {
                _logger.Error("FlashWindowEx failed.", ex);
            }
        }
    }
}
