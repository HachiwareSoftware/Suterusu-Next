using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Forms;
using Suterusu.Models;

namespace Suterusu.Configuration
{
    internal static class HotkeyBindingHelper
    {
        private static readonly Keys[] SupportedPrimaryKeys = BuildSupportedPrimaryKeys();

        public static string GetDefaultBinding(GlobalHotkey hotkey)
        {
            switch (hotkey)
            {
                case GlobalHotkey.ClearHistory:
                    return "F6";
                case GlobalHotkey.SendClipboard:
                    return "F7";
                case GlobalHotkey.RunOcr:
                    return "Shift+F7";
                case GlobalHotkey.CopyLastResponse:
                    return "F8";
                case GlobalHotkey.QuitApplication:
                    return "F12";
                default:
                    throw new ArgumentOutOfRangeException(nameof(hotkey));
            }
        }

        public static string NormalizeBindingName(string bindingName, GlobalHotkey hotkey)
        {
            return TryParseBinding(bindingName, out HotkeyBinding binding)
                ? binding.ToDisplayString()
                : GetDefaultBinding(hotkey);
        }

        public static bool TryParseBinding(string bindingName, out HotkeyBinding binding)
        {
            binding = null;
            string trimmed = (bindingName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            string[] tokens = trimmed.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .ToArray();

            if (tokens.Length == 0)
                return false;

            bool control = false;
            bool alt = false;
            bool shift = false;
            bool windows = false;
            Keys primaryKey = Keys.None;

            foreach (string token in tokens)
            {
                string normalized = token.ToUpperInvariant();
                switch (normalized)
                {
                    case "CTRL":
                    case "CONTROL":
                        if (control)
                            return false;
                        control = true;
                        break;

                    case "ALT":
                        if (alt)
                            return false;
                        alt = true;
                        break;

                    case "SHIFT":
                        if (shift)
                            return false;
                        shift = true;
                        break;

                    case "WIN":
                    case "WINDOWS":
                        if (windows)
                            return false;
                        windows = true;
                        break;

                    default:
                        if (primaryKey != Keys.None)
                            return false;

                        if (!TryParsePrimaryKey(normalized, out primaryKey))
                            return false;
                        break;
                }
            }

            if (primaryKey == Keys.None)
                return false;

            binding = new HotkeyBinding(primaryKey, control, alt, shift, windows);
            return true;
        }

        public static bool IsSupportedBindingName(string bindingName)
        {
            return TryParseBinding(bindingName, out _);
        }

        public static IReadOnlyList<string> GetDuplicateBindingErrors(
            string clearHistoryBinding,
            string sendClipboardBinding,
            string copyLastResponseBinding,
            string quitApplicationBinding,
            string ocrBinding)
        {
            var bindings = new[]
            {
                NormalizeIfValid(clearHistoryBinding),
                NormalizeIfValid(sendClipboardBinding),
                NormalizeIfValid(copyLastResponseBinding),
                NormalizeIfValid(quitApplicationBinding),
                NormalizeIfValid(ocrBinding)
            };

            return bindings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => $"Hotkey '{group.Key}' is assigned more than once.")
                .ToList();
        }

        public static bool TryBuildBindingFromKeyEvent(Key key, ModifierKeys modifiers, out string bindingName)
        {
            bindingName = null;

            if (key == Key.Escape)
                return false;

            if (!TryConvertWpfKey(key, out Keys primaryKey))
                return false;

            if (IsModifierKey(primaryKey))
                return false;

            var binding = new HotkeyBinding(
                primaryKey,
                (modifiers & ModifierKeys.Control) == ModifierKeys.Control,
                (modifiers & ModifierKeys.Alt) == ModifierKeys.Alt,
                (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift,
                (modifiers & ModifierKeys.Windows) == ModifierKeys.Windows);

            bindingName = binding.ToDisplayString();
            return true;
        }

        public static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey
                || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu
                || key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey
                || key == Keys.LWin || key == Keys.RWin;
        }

        public static bool IsControlKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey;
        }

        public static bool IsAltKey(Keys key)
        {
            return key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;
        }

        public static bool IsShiftKey(Keys key)
        {
            return key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey;
        }

        public static bool IsWindowsKey(Keys key)
        {
            return key == Keys.LWin || key == Keys.RWin;
        }

        private static string NormalizeIfValid(string bindingName)
        {
            return TryParseBinding(bindingName, out HotkeyBinding binding)
                ? binding.ToDisplayString()
                : null;
        }

        private static bool TryParsePrimaryKey(string token, out Keys key)
        {
            key = Keys.None;

            if (!Enum.TryParse(token, ignoreCase: true, result: out key))
                return false;

            return SupportedPrimaryKeys.Contains(key);
        }

        private static bool TryConvertWpfKey(Key key, out Keys formsKey)
        {
            formsKey = Keys.None;

            switch (key)
            {
                case Key.LeftCtrl:
                    formsKey = Keys.LControlKey;
                    return true;
                case Key.RightCtrl:
                    formsKey = Keys.RControlKey;
                    return true;
                case Key.LeftAlt:
                    formsKey = Keys.LMenu;
                    return true;
                case Key.RightAlt:
                    formsKey = Keys.RMenu;
                    return true;
                case Key.LeftShift:
                    formsKey = Keys.LShiftKey;
                    return true;
                case Key.RightShift:
                    formsKey = Keys.RShiftKey;
                    return true;
                case Key.LWin:
                    formsKey = Keys.LWin;
                    return true;
                case Key.RWin:
                    formsKey = Keys.RWin;
                    return true;
                default:
                    try
                    {
                        formsKey = (Keys)KeyInterop.VirtualKeyFromKey(key);
                        return formsKey != Keys.None;
                    }
                    catch
                    {
                        return false;
                    }
            }
        }

        private static Keys[] BuildSupportedPrimaryKeys()
        {
            var keys = new List<Keys>();

            for (int i = (int)Keys.A; i <= (int)Keys.Z; i++)
                keys.Add((Keys)i);

            for (int i = (int)Keys.D0; i <= (int)Keys.D9; i++)
                keys.Add((Keys)i);

            for (int i = (int)Keys.F1; i <= (int)Keys.F24; i++)
                keys.Add((Keys)i);

            keys.AddRange(new[]
            {
                Keys.Tab,
                Keys.Space,
                Keys.Return,
                Keys.Back,
                Keys.Insert,
                Keys.Delete,
                Keys.Home,
                Keys.End,
                Keys.PageUp,
                Keys.PageDown,
                Keys.Up,
                Keys.Down,
                Keys.Left,
                Keys.Right,
                Keys.Oem1,
                Keys.Oem2,
                Keys.Oem3,
                Keys.Oem4,
                Keys.Oem5,
                Keys.Oem6,
                Keys.Oem7,
                Keys.Oem8,
                Keys.Oem102,
                Keys.OemClear,
                Keys.Oemcomma,
                Keys.OemMinus,
                Keys.OemPeriod,
                Keys.Oemplus
            });

            return keys.ToArray();
        }
    }
}
