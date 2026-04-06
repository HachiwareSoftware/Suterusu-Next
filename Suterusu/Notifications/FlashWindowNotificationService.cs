using System;
using Suterusu.Interop;
using Suterusu.Services;

namespace Suterusu.Notifications
{
    /// <summary>Flashes the current foreground window taskbar button to signal completion.</summary>
    public class FlashWindowNotificationService : INotificationService
    {
        private readonly ILogger _logger = new NLogLogger("Suterusu.Notification.Flash");

        public void NotifySuccess()
        {
            Flash(3);
        }

        public void NotifyFailure()
        {
            Flash(5);
        }

        private void Flash(uint count)
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;

                var info = new NativeMethods.FLASHWINFO
                {
                    cbSize   = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.FLASHWINFO)),
                    hwnd     = hwnd,
                    dwFlags  = NativeMethods.FLASHW_ALL | NativeMethods.FLASHW_TIMERNOFG,
                    uCount   = count,
                    dwTimeout = 0
                };

                NativeMethods.FlashWindowEx(ref info);
            }
            catch (Exception ex)
            {
                _logger.Error("FlashWindowEx failed.", ex);
            }
        }
    }
}
