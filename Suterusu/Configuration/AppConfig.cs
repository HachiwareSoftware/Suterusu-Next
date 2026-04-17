using System.Collections.Generic;
using System.Linq;
using Suterusu.Models;

namespace Suterusu.Configuration
{
    public class OcrSettings
    {
        public bool Enabled { get; set; }
        public string Hotkey { get; set; }
        public OcrProvider Provider { get; set; }
        public string Prompt { get; set; }
        public int TimeoutMs { get; set; }

        // llama.cpp settings
        public string LlamaCppUrl { get; set; }
        public string LlamaCppModel { get; set; }

        // Z.ai settings
        public string ZaiToken { get; set; }
        public string ZaiModel { get; set; }

        // Custom (OpenAI-compatible) settings
        public string CustomUrl { get; set; }
        public string CustomApiKey { get; set; }
        public string CustomModel { get; set; }

        // HuggingFace settings
        public string HfToken { get; set; }
        public string HfModel { get; set; }
        public string HfUrl { get; set; }

        // Clipboard prompt option
        public bool UseClipboardPrompt { get; set; }

        public static OcrSettings CreateDefault() => new OcrSettings
        {
            Enabled = false,
            Hotkey = "Shift+F7",
            Provider = OcrProvider.LlamaCpp,
            Prompt = "Recognize all text from this image.",
            TimeoutMs = 60000,
            LlamaCppUrl = "http://localhost:8080",
            LlamaCppModel = "ggml-org/GLM-OCR-GGUF",
            ZaiToken = "",
            ZaiModel = "glm-ocr",
            CustomUrl = "",
            CustomApiKey = "",
            CustomModel = "",
            HfToken = "",
            HfModel = "google/ocr",
            HfUrl = "https://api.huggingface.co/v1",
            UseClipboardPrompt = false
        };
    }

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

        public OcrSettings Ocr { get; set; }

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
                Ocr                     = OcrSettings.CreateDefault()
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

            if (Ocr == null)
                Ocr = OcrSettings.CreateDefault();

            Ocr.Hotkey = HotkeyBindingHelper.NormalizeBindingName(
                Ocr.Hotkey,
                GlobalHotkey.RunOcr);

            if (Ocr.TimeoutMs <= 0)
                Ocr.TimeoutMs = 60000;

            if (Ocr.TimeoutMs > 120000)
                Ocr.TimeoutMs = 120000;

            if (string.IsNullOrWhiteSpace(Ocr.LlamaCppModel))
                Ocr.LlamaCppModel = "ggml-org/GLM-OCR-GGUF";

            if (string.IsNullOrWhiteSpace(Ocr.LlamaCppUrl))
                Ocr.LlamaCppUrl = "http://localhost:8080";

            if (string.IsNullOrWhiteSpace(Ocr.ZaiModel))
                Ocr.ZaiModel = "glm-ocr";

            if (string.IsNullOrWhiteSpace(Ocr.HfModel))
                Ocr.HfModel = "google/ocr";

            if (string.IsNullOrWhiteSpace(Ocr.HfUrl))
                Ocr.HfUrl = "https://api.huggingface.co/v1";

            if (HotkeyBindingHelper.GetDuplicateBindingErrors(
                ClearHistoryHotkey,
                SendClipboardHotkey,
                CopyLastResponseHotkey,
                QuitApplicationHotkey,
                Ocr.Hotkey).Count > 0)
            {
                ClearHistoryHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.ClearHistory);
                SendClipboardHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.SendClipboard);
                CopyLastResponseHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.CopyLastResponse);
                QuitApplicationHotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.QuitApplication);
                Ocr.Hotkey = HotkeyBindingHelper.GetDefaultBinding(GlobalHotkey.RunOcr);
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

            if (!HotkeyBindingHelper.IsSupportedBindingName(Ocr?.Hotkey))
                errors.Add("OCR hotkey must be a valid key combination.");

            if (Ocr?.Enabled == true)
            {
                if (Ocr.Provider == OcrProvider.LlamaCpp)
                {
                    if (string.IsNullOrWhiteSpace(Ocr.LlamaCppUrl))
                        errors.Add("llama.cpp URL required when OCR is enabled.");
                }
                else if (Ocr.Provider == OcrProvider.Zai)
                {
                    if (string.IsNullOrWhiteSpace(Ocr.ZaiToken))
                        errors.Add("Z.ai API token required when OCR is enabled.");
                }
                else if (Ocr.Provider == OcrProvider.Custom)
                {
                    if (string.IsNullOrWhiteSpace(Ocr.CustomUrl))
                        errors.Add("Custom URL required when OCR is enabled.");
                    if (string.IsNullOrWhiteSpace(Ocr.CustomApiKey))
                        errors.Add("Custom API key required when OCR is enabled.");
                    if (string.IsNullOrWhiteSpace(Ocr.CustomModel))
                        errors.Add("Custom model required when OCR is enabled.");
                }
                else if (Ocr.Provider == OcrProvider.HuggingFace)
                {
                    if (string.IsNullOrWhiteSpace(Ocr.HfToken))
                        errors.Add("HuggingFace token required when OCR is enabled.");
                }
            }

            foreach (string duplicateError in HotkeyBindingHelper.GetDuplicateBindingErrors(
                ClearHistoryHotkey,
                SendClipboardHotkey,
                CopyLastResponseHotkey,
                QuitApplicationHotkey,
                Ocr?.Hotkey))
            {
                errors.Add(duplicateError);
            }

            return errors;
        }
    }
}
