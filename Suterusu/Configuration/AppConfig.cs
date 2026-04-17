using System.Collections.Generic;
using System.Linq;
using Suterusu.Models;

namespace Suterusu.Configuration
{
    public class AppConfig
    {
        /// <summary>
        /// Flat ordered list of (endpoint, model) pairs used by all multi-request modes.
        /// Models are tried top-to-bottom in Sequential mode, rotated in RoundRobin, raced in Fastest.
        /// </summary>
        public List<ModelEntry> ModelPriority { get; set; }

        public string SystemPrompt { get; set; }

        public int HistoryLimit { get; set; }

        public NotificationMode NotificationMode { get; set; }

        public MultiRequestMode MultiRequestMode { get; set; }

        public int MultiRequestTimeoutMs { get; set; }

        public int RoundRobinIndex { get; set; }

        /// <summary>
        /// Process name (or partial name) of the window to flash, e.g. "Chrome".
        /// Use "All" to flash every visible window, or "None" / empty to disable.
        /// </summary>
        public string FlashWindowTarget { get; set; }

        /// <summary>
        /// How long (in milliseconds) to let the flash run before sending FLASHW_STOP.
        /// </summary>
        public int FlashWindowDurationMs { get; set; }

        public int CircleDotBlinkCount { get; set; }

        public int CircleDotBlinkDurationMs { get; set; }

        public string ClearHistoryHotkey { get; set; }

        public string SendClipboardHotkey { get; set; }

        public string CopyLastResponseHotkey { get; set; }

        public string QuitApplicationHotkey { get; set; }

        public bool OcrEnabled { get; set; }

        public string OcrHotkey { get; set; }

        public OcrProvider OcrProvider { get; set; }

        public string OcrPrompt { get; set; }

        public int OcrTimeoutMs { get; set; }

        public string OcrHfToken { get; set; }

        public string OcrHfModel { get; set; }

        public string OcrLlamaCppUrl { get; set; }

        public string OcrLlamaCppModel { get; set; }

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                ModelPriority            = new List<ModelEntry>(),
                SystemPrompt             = "You are a helpful assistant.",
                HistoryLimit             = 10,
                NotificationMode         = NotificationMode.FlashWindow,
                MultiRequestMode         = MultiRequestMode.RoundRobin,
                MultiRequestTimeoutMs    = 60000,
                RoundRobinIndex          = 0,
                FlashWindowTarget        = "Chrome",
                FlashWindowDurationMs    = 1600,
                CircleDotBlinkCount      = 3,
                CircleDotBlinkDurationMs = 600,
                ClearHistoryHotkey       = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.ClearHistory),
                SendClipboardHotkey      = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.SendClipboard),
                CopyLastResponseHotkey   = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.CopyLastResponse),
                QuitApplicationHotkey    = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.QuitApplication),
                OcrEnabled               = false,
                OcrHotkey                = "Shift+F7",
                OcrProvider              = OcrProvider.HuggingFace,
                OcrPrompt                = "Recognize all text from this image.",
                OcrTimeoutMs             = 60000,
                OcrHfToken               = string.Empty,
                OcrHfModel               = "zai-org/GLM-OCR",
                OcrLlamaCppUrl           = "http://localhost:8080",
                OcrLlamaCppModel         = "GLM-OCR"
            };
        }

        public AppConfig Normalize()
        {
            if (string.IsNullOrWhiteSpace(SystemPrompt))
                SystemPrompt = "You are a helpful assistant.";

            if (HistoryLimit < 0)
                HistoryLimit = 0;

            if (HistoryLimit > 100)
                HistoryLimit = 100;

            if (string.IsNullOrWhiteSpace(FlashWindowTarget))
                FlashWindowTarget = "Chrome";

            if (FlashWindowDurationMs <= 0)
                FlashWindowDurationMs = 1600;

            if (FlashWindowDurationMs > 10000)
                FlashWindowDurationMs = 10000;

            if (MultiRequestTimeoutMs <= 0)
                MultiRequestTimeoutMs = 60000;

            if (MultiRequestTimeoutMs > 120000)
                MultiRequestTimeoutMs = 120000;

            if (CircleDotBlinkCount < 1)
                CircleDotBlinkCount = 3;

            if (CircleDotBlinkCount > 10)
                CircleDotBlinkCount = 10;

            if (CircleDotBlinkDurationMs < 200)
                CircleDotBlinkDurationMs = 200;

            if (CircleDotBlinkDurationMs > 5000)
                CircleDotBlinkDurationMs = 5000;

            ClearHistoryHotkey = HotkeyBindingHelper.NormalizeBindingName(
                ClearHistoryHotkey,
                GlobalHotkey.ClearHistory);
            SendClipboardHotkey = HotkeyBindingHelper.NormalizeBindingName(
                SendClipboardHotkey,
                GlobalHotkey.SendClipboard);
            CopyLastResponseHotkey = HotkeyBindingHelper.NormalizeBindingName(
                CopyLastResponseHotkey,
                GlobalHotkey.CopyLastResponse);
            QuitApplicationHotkey = HotkeyBindingHelper.NormalizeBindingName(
                QuitApplicationHotkey,
                GlobalHotkey.QuitApplication);

            OcrHotkey = HotkeyBindingHelper.NormalizeBindingName(
                OcrHotkey,
                GlobalHotkey.RunOcr);

            if (OcrTimeoutMs <= 0)
                OcrTimeoutMs = 60000;

            if (OcrTimeoutMs > 120000)
                OcrTimeoutMs = 120000;

            if (string.IsNullOrWhiteSpace(OcrHfModel))
                OcrHfModel = "zai-org/GLM-OCR";

            if (string.IsNullOrWhiteSpace(OcrLlamaCppModel))
                OcrLlamaCppModel = "GLM-OCR";

            if (string.IsNullOrWhiteSpace(OcrLlamaCppUrl))
                OcrLlamaCppUrl = "http://localhost:8080";

            if (HotkeyBindingHelper.GetDuplicateBindingErrors(
                ClearHistoryHotkey,
                SendClipboardHotkey,
                CopyLastResponseHotkey,
                QuitApplicationHotkey,
                OcrHotkey).Count > 0)
            {
                ClearHistoryHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.ClearHistory);
                SendClipboardHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.SendClipboard);
                CopyLastResponseHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.CopyLastResponse);
                QuitApplicationHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.QuitApplication);
                OcrHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.RunOcr);
            }

            if (RoundRobinIndex < 0)
                RoundRobinIndex = 0;

            if (ModelPriority == null)
                ModelPriority = new List<ModelEntry>();

            ModelPriority = ModelPriority
                .Where(e => !string.IsNullOrWhiteSpace(e.BaseUrl) && !string.IsNullOrWhiteSpace(e.Model))
                .ToList();

            return this;
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (ModelPriority == null || ModelPriority.Count == 0)
            {
                errors.Add("At least one entry must be added to the Model Priority list.");
            }
            else
            {
                for (int i = 0; i < ModelPriority.Count; i++)
                {
                    var entry = ModelPriority[i];
                    if (string.IsNullOrWhiteSpace(entry.BaseUrl))
                        errors.Add($"Entry {i + 1} ({entry.Name}) has no Base URL.");
                    if (string.IsNullOrWhiteSpace(entry.Model))
                        errors.Add($"Entry {i + 1} ({entry.Name}) has no model specified.");
                }
            }

            if (!HotkeyBindingHelper.IsSupportedBindingName(ClearHistoryHotkey))
                errors.Add("Clear history hotkey must be a valid key combination.");

            if (!HotkeyBindingHelper.IsSupportedBindingName(SendClipboardHotkey))
                errors.Add("Send clipboard hotkey must be a valid key combination.");

            if (!HotkeyBindingHelper.IsSupportedBindingName(CopyLastResponseHotkey))
                errors.Add("Copy last response hotkey must be a valid key combination.");

            if (!HotkeyBindingHelper.IsSupportedBindingName(QuitApplicationHotkey))
                errors.Add("Quit application hotkey must be a valid key combination.");

            if (!HotkeyBindingHelper.IsSupportedBindingName(OcrHotkey))
                errors.Add("OCR hotkey must be a valid key combination.");

            if (OcrEnabled)
            {
                if (OcrProvider == OcrProvider.HuggingFace)
                {
                    if (string.IsNullOrWhiteSpace(OcrHfToken))
                        errors.Add("HuggingFace token required when OCR is enabled.");
                }
                else if (OcrProvider == OcrProvider.LlamaCpp)
                {
                    if (string.IsNullOrWhiteSpace(OcrLlamaCppUrl))
                        errors.Add("llama.cpp URL required when OCR is enabled.");
                }
            }

            foreach (string duplicateError in HotkeyBindingHelper.GetDuplicateBindingErrors(
                ClearHistoryHotkey,
                SendClipboardHotkey,
                CopyLastResponseHotkey,
                QuitApplicationHotkey,
                OcrHotkey))
            {
                errors.Add(duplicateError);
            }

            return errors;
        }
    }
}
