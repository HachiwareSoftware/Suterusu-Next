using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Suterusu.Configuration;
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
        private sealed class RegisteredHotkey
        {
            public RegisteredHotkey(HotkeyBinding binding, GlobalHotkey action)
            {
                Binding = binding;
                Action = action;
            }

            public HotkeyBinding Binding { get; }

            public GlobalHotkey Action { get; }
        }

        private readonly ILogger _logger;
        private NativeMethods.LowLevelKeyboardProc _proc; // keep alive to prevent GC
        private IntPtr _hookHandle = IntPtr.Zero;
        private Dictionary<Keys, List<RegisteredHotkey>> _bindings = new Dictionary<Keys, List<RegisteredHotkey>>();

        // Tracks keys currently held down to suppress repeats
        private readonly HashSet<Keys> _pressedKeys = new HashSet<Keys>();

        public bool IsInstalled => _hookHandle != IntPtr.Zero;

        /// <summary>Raised on the message-pump thread when a configured hotkey fires.</summary>
        public event EventHandler<GlobalHotkey> HotkeyTriggered;

        public KeyboardHook(ILogger logger)
        {
            _logger = logger;
        }

        public void UpdateBindings(AppConfig config)
        {
            var bindings = new Dictionary<Keys, List<RegisteredHotkey>>();

            AddBinding(bindings, config.ClearHistoryHotkey, GlobalHotkey.ClearHistory);
            AddBinding(bindings, config.SendClipboardHotkey, GlobalHotkey.SendClipboard);
            AddBinding(bindings, config.CopyLastResponseHotkey, GlobalHotkey.CopyLastResponse);
            AddBinding(bindings, config.QuitApplicationHotkey, GlobalHotkey.QuitApplication);

            _bindings = bindings;
            _pressedKeys.Clear();
            _logger.Info(
                $"Hotkeys updated: {config.ClearHistoryHotkey}=ClearHistory, {config.SendClipboardHotkey}=SendClipboard, {config.CopyLastResponseHotkey}=CopyLastResponse, {config.QuitApplicationHotkey}=QuitApplication");
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

                if (isKeyDown)
                {
                    if (_pressedKeys.Contains(key))
                        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

                    if (ShouldHandleKeyDown(key, out RegisteredHotkey hotkey))
                    {
                        try { HotkeyTriggered?.Invoke(this, hotkey.Action); }
                        catch (Exception ex)
                        {
                            _logger.Error("Error in HotkeyTriggered handler.", ex);
                        }
                    }

                    _pressedKeys.Add(key);
                }
                else if (isKeyUp)
                {
                    _pressedKeys.Remove(key);
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private bool ShouldHandleKeyDown(Keys key, out RegisteredHotkey hotkey)
        {
            hotkey = null;

            if (!_bindings.TryGetValue(key, out List<RegisteredHotkey> registeredHotkeys))
                return false;

            if (HotkeyBindingHelper.IsModifierKey(key))
                return false;

            bool controlPressed = _pressedKeys.Any(HotkeyBindingHelper.IsControlKey);
            bool altPressed = _pressedKeys.Any(HotkeyBindingHelper.IsAltKey);
            bool shiftPressed = _pressedKeys.Any(HotkeyBindingHelper.IsShiftKey);
            bool windowsPressed = _pressedKeys.Any(HotkeyBindingHelper.IsWindowsKey);

            RegisteredHotkey registeredHotkey = registeredHotkeys.FirstOrDefault(candidate =>
            {
                HotkeyBinding binding = candidate.Binding;
                return binding.Control == controlPressed
                    && binding.Alt == altPressed
                    && binding.Shift == shiftPressed
                    && binding.Windows == windowsPressed;
            });

            if (registeredHotkey == null)
            {
                return false;
            }

            hotkey = registeredHotkey;
            return true;
        }

        private static void AddBinding(Dictionary<Keys, List<RegisteredHotkey>> bindings, string bindingName, GlobalHotkey action)
        {
            if (!HotkeyBindingHelper.TryParseBinding(bindingName, out HotkeyBinding binding))
                throw new ArgumentOutOfRangeException(nameof(bindingName), $"Unsupported hotkey '{bindingName}'.");

            if (!bindings.TryGetValue(binding.PrimaryKey, out List<RegisteredHotkey> entries))
            {
                entries = new List<RegisteredHotkey>();
                bindings.Add(binding.PrimaryKey, entries);
            }

            entries.Add(new RegisteredHotkey(binding, action));
        }

        public void Dispose()
        {
            Uninstall();
        }
    }
}
