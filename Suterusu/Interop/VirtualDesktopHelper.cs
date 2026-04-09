using System;
using System.Runtime.InteropServices;
using Suterusu.Services;

namespace Suterusu.Interop
{
    internal static class VirtualDesktopHelper
    {
        private static readonly ILogger Logger = new NLogLogger("Suterusu.Interop.VirtualDesktop");

        public static bool TryInitializeComForCurrentThread(out bool shouldUninitialize)
        {
            shouldUninitialize = false;

            int hr = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_APARTMENTTHREADED);
            if (hr == NativeMethods.S_OK)
            {
                shouldUninitialize = true;
                return true;
            }

            if (hr == NativeMethods.S_FALSE)
            {
                return true;
            }

            if (hr == NativeMethods.RPC_E_CHANGED_MODE)
            {
                Logger.Warn("COM apartment already initialized with a different mode; skipping virtual desktop move.");
                return false;
            }

            Logger.Warn($"CoInitializeEx failed with HRESULT 0x{hr:X8}.");
            return false;
        }

        public static void UninitializeComForCurrentThread(bool shouldUninitialize)
        {
            if (shouldUninitialize)
            {
                NativeMethods.CoUninitialize();
            }
        }

        public static bool MoveWindowToCurrentDesktop(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            IVirtualDesktopManager manager = null;

            try
            {
                manager = CreateManager();
                if (manager == null)
                {
                    return false;
                }

                IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                {
                    Logger.Debug("No foreground window available to infer the active desktop.");
                    return false;
                }

                Guid activeDesktopId;
                int getDesktopHr = manager.GetWindowDesktopId(foregroundWindow, out activeDesktopId);
                if (getDesktopHr != NativeMethods.S_OK)
                {
                    Logger.Debug($"GetWindowDesktopId failed with HRESULT 0x{getDesktopHr:X8}.");
                    return false;
                }

                int moveHr = manager.MoveWindowToDesktop(windowHandle, ref activeDesktopId);
                if (moveHr != NativeMethods.S_OK)
                {
                    Logger.Debug($"MoveWindowToDesktop failed with HRESULT 0x{moveHr:X8}.");
                    return false;
                }

                return true;
            }
            catch (COMException ex)
            {
                Logger.Debug($"Virtual desktop move failed with HRESULT 0x{ex.HResult:X8}.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("Unexpected virtual desktop move failure.", ex);
                return false;
            }
            finally
            {
                if (manager != null)
                {
                    Marshal.FinalReleaseComObject(manager);
                }
            }
        }

        private static IVirtualDesktopManager CreateManager()
        {
            object managerObject;
            Guid clsid = NativeMethods.CLSID_VirtualDesktopManager;
            Guid iid = NativeMethods.IID_IVirtualDesktopManager;

            int hr = NativeMethods.CoCreateInstance(
                ref clsid,
                IntPtr.Zero,
                NativeMethods.CLSCTX_INPROC_SERVER,
                ref iid,
                out managerObject);

            if (hr != NativeMethods.S_OK || managerObject == null)
            {
                Logger.Debug($"CoCreateInstance for VirtualDesktopManager failed with HRESULT 0x{hr:X8}.");
                return null;
            }

            return (IVirtualDesktopManager)managerObject;
        }
    }
}
