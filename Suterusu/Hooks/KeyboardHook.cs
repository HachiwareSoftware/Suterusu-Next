using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Suterusu.Interop;
using Suterusu.Models;
using Suterusu.Services;

namespace Suterusu.Hooks
{
    /// <summary>
    /// Installs a WH_KEYBOARD_LL hook and raises a HotkeyTriggered event exactly once per
    /// key-down for the configured hotkeys, preventing held-key repeats.
    /// </summary>
    public class KeyboardHook : IDisposable
    {
        private readonly ILogger _logger;
        private NativeMethods.LowLevelKeyboardProc _proc; // keep alive to prevent GC
        private IntPtr _hookHandle = IntPtr.Zero;

        // Tracks keys currently held down to suppress repeats
        private readonly HashSet<Keys> _pressedKeys = new HashSet<Keys>();

        public bool IsInstalled => _hookHandle != IntPtr.Zero;

        /// <summary>Raised on the message-pump thread when a configured hotkey fires.</summary>
        public event EventHandler<GlobalHotkey> HotkeyTriggered;

        // Hotkeys we care about
        private static readonly Keys[] MonitoredKeys = { Keys.F6, Keys.F7, Keys.F8, Keys.F12 };

        public KeyboardHook(ILogger logger)
        {
            _logger = logger;
        }

        public void Install()
        {
            if (IsInstalled)
                throw new InvalidOperationException("Hook already installed.");

            _proc = HookCallback;

            using (Process proc = Process.GetCurrentProcess())
            using (ProcessModule module = proc.MainModule)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(module.ModuleName);
                _hookHandle = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
            }

            if (_hookHandle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"SetWindowsHookEx failed with Win32 error {err}.");
            }

            _logger.Info("Keyboard hook installed.");
        }

        public void Uninstall()
        {
            if (!IsInstalled) return;

            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _logger.Info("Keyboard hook uninstalled.");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isKeyDown   = (msg == NativeMethods.WM_KEYDOWN   || msg == NativeMethods.WM_SYSKEYDOWN);
                bool isKeyUp     = (msg == NativeMethods.WM_KEYUP     || msg == NativeMethods.WM_SYSKEYUP);

                var kbInfo = (NativeMethods.KBDLLHOOKSTRUCT)
                    Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));

                var key = (Keys)kbInfo.vkCode;

                if (isKeyDown && ShouldHandleKeyDown(key))
                {
                    _pressedKeys.Add(key);
                    GlobalHotkey hotkey = KeyToHotkey(key);
                    try { HotkeyTriggered?.Invoke(this, hotkey); }
                    catch (Exception ex)
                    {
                        _logger.Error("Error in HotkeyTriggered handler.", ex);
                    }
                }
                else if (isKeyUp)
                {
                    _pressedKeys.Remove(key);
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private bool ShouldHandleKeyDown(Keys key)
        {
            if (!IsMonitored(key)) return false;
            // Suppress if already pressed (key repeat)
            return !_pressedKeys.Contains(key);
        }

        private static bool IsMonitored(Keys key)
        {
            foreach (Keys k in MonitoredKeys)
                if (k == key) return true;
            return false;
        }

        private static GlobalHotkey KeyToHotkey(Keys key)
        {
            switch (key)
            {
                case Keys.F6:  return GlobalHotkey.F6;
                case Keys.F7:  return GlobalHotkey.F7;
                case Keys.F8:  return GlobalHotkey.F8;
                case Keys.F12: return GlobalHotkey.F12;
                default:       throw new ArgumentOutOfRangeException(nameof(key));
            }
        }

        public void Dispose()
        {
            Uninstall();
        }
    }
}
