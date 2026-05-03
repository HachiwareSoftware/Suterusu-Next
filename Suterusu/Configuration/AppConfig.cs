using System;
using System.Collections.Generic;
using System.IO;
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

        // Windows OCR settings (no API key / server needed)
        /// <summary>
        /// BCP-47 language tag for Windows OCR (e.g. "en-US"). Empty string = auto from user profile.
        /// </summary>
        public string WindowsOcrLanguage { get; set; }

        public int MaxTokens { get; set; }

        public bool DownscaleImage { get; set; }

        public int MaxImageDimension { get; set; }

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
            UseClipboardPrompt = false,
            WindowsOcrLanguage = "",
            MaxTokens = 4096,
            DownscaleImage = true,
            MaxImageDimension = 1024
        };
    }

    public class CdpSettings
    {
        public bool Enabled { get; set; }

        public int Port { get; set; }

        public string UrlPattern { get; set; }

        public string StartupScriptsDirectory { get; set; }

        public int RetryIntervalMs { get; set; }

        public int ConnectTimeoutMs { get; set; }

        public bool InjectOnStartup { get; set; }

        public static CdpSettings CreateDefault() => new CdpSettings
        {
            Enabled = false,
            Port = 27245,
            UrlPattern = "",
            StartupScriptsDirectory = "js/events",
            RetryIntervalMs = 5000,
            ConnectTimeoutMs = 2000,
            InjectOnStartup = true
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

        public CliProxySettings CliProxy { get; set; }

        public CdpSettings Cdp { get; set; }

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
                Ocr                      = OcrSettings.CreateDefault(),
                CliProxy                 = CliProxySettings.CreateDefault(),
                Cdp                      = CdpSettings.CreateDefault()
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

            if (Ocr.MaxTokens < 1)
                Ocr.MaxTokens = 4096;

            if (Ocr.MaxTokens > 65536)
                Ocr.MaxTokens = 65536;

            if (Ocr.MaxImageDimension < 64)
                Ocr.MaxImageDimension = 1024;

            if (Ocr.MaxImageDimension > 4096)
                Ocr.MaxImageDimension = 4096;

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

            if (Ocr.WindowsOcrLanguage != null)
                Ocr.WindowsOcrLanguage = Ocr.WindowsOcrLanguage.Trim();
            else
                Ocr.WindowsOcrLanguage = "";

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

            NormalizeCliProxySettings();
            NormalizeCdpSettings();
            SyncCliProxyModelEntry();

            return this;
        }

        private void NormalizeCdpSettings()
        {
            if (Cdp == null)
                Cdp = CdpSettings.CreateDefault();

            if (Cdp.Port <= 0 || Cdp.Port > 65535)
                Cdp.Port = 27245;

            if (Cdp.UrlPattern == null)
                Cdp.UrlPattern = "";
            else
                Cdp.UrlPattern = Cdp.UrlPattern.Trim();

            if (string.IsNullOrWhiteSpace(Cdp.StartupScriptsDirectory))
                Cdp.StartupScriptsDirectory = "js/events";
            else
                Cdp.StartupScriptsDirectory = Cdp.StartupScriptsDirectory.Trim();

            if (string.Equals(Cdp.StartupScriptsDirectory, "js/startup", StringComparison.OrdinalIgnoreCase))
                Cdp.StartupScriptsDirectory = "js/events";

            if (Cdp.RetryIntervalMs < 1)
                Cdp.RetryIntervalMs = 1;

            if (Cdp.RetryIntervalMs > 177013)
                Cdp.RetryIntervalMs = 177013;

            if (Cdp.ConnectTimeoutMs < 1)
                Cdp.ConnectTimeoutMs = 1;

            if (Cdp.ConnectTimeoutMs > 177013)
                Cdp.ConnectTimeoutMs = 177013;
        }

        public bool HasConfiguredChatTarget()
        {
            return (ModelPriority != null && ModelPriority.Count > 0)
                || CliProxy?.Enabled == true;
        }

        private void NormalizeCliProxySettings()
        {
            if (CliProxy == null)
                CliProxy = CliProxySettings.CreateDefault();

            bool migrateLegacyRuntimeDirectory = CliProxySettings.IsLegacyRuntimeDirectory(CliProxy.RuntimeDirectory);

            if (string.IsNullOrWhiteSpace(CliProxy.RuntimeDirectory) || migrateLegacyRuntimeDirectory)
                CliProxy.RuntimeDirectory = CliProxySettings.GetDefaultRuntimeDirectory();

            CliProxy.RuntimeDirectory = CliProxy.RuntimeDirectory.Trim();

            if (string.IsNullOrWhiteSpace(CliProxy.ExecutablePath)
                || CliProxySettings.IsLegacyExecutablePath(CliProxy.ExecutablePath)
                || migrateLegacyRuntimeDirectory)
                CliProxy.ExecutablePath = CliProxySettings.GetExecutablePath(CliProxy.RuntimeDirectory);

            if (string.IsNullOrWhiteSpace(CliProxy.ConfigPath) || migrateLegacyRuntimeDirectory)
                CliProxy.ConfigPath = Path.Combine(CliProxy.RuntimeDirectory, "config.yaml");

            if (string.IsNullOrWhiteSpace(CliProxy.AuthDirectory) || migrateLegacyRuntimeDirectory)
                CliProxy.AuthDirectory = Path.Combine(CliProxy.RuntimeDirectory, "auths");

            if (string.IsNullOrWhiteSpace(CliProxy.Host) || !IsLocalHost(CliProxy.Host))
                CliProxy.Host = "127.0.0.1";

            if (CliProxy.Port <= 0 || CliProxy.Port > 65535)
                CliProxy.Port = 8317;

            if (CliProxy.OAuthCallbackPort <= 0 || CliProxy.OAuthCallbackPort > 65535)
                CliProxy.OAuthCallbackPort = 1455;

            if (string.IsNullOrWhiteSpace(CliProxy.Model))
                CliProxy.Model = CliProxySettings.DefaultCodexModel;

            if (string.IsNullOrWhiteSpace(CliProxy.ApiKey))
                CliProxy.ApiKey = CliProxySettings.GenerateSecret(24);

            if (string.IsNullOrWhiteSpace(CliProxy.ManagementKey))
                CliProxy.ManagementKey = CliProxySettings.GenerateSecret(24);

            if (string.Equals(CliProxy.ApiKey, CliProxy.ManagementKey, StringComparison.Ordinal))
                CliProxy.ManagementKey = CliProxySettings.GenerateSecret(24);

            if (!CliProxySettings.IsGeminiProvider(CliProxy.Provider))
                CliProxy.Provider = CliProxySettings.CodexProvider;

            if (CliProxy.GeminiProjectId == null)
                CliProxy.GeminiProjectId = "";
            else
                CliProxy.GeminiProjectId = CliProxy.GeminiProjectId.Trim();

            if (string.IsNullOrWhiteSpace(CliProxy.Model)
                || (CliProxySettings.IsGeminiProvider(CliProxy.Provider)
                    && (string.Equals(CliProxy.Model, CliProxySettings.DefaultCodexModel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(CliProxy.Model, CliProxySettings.LegacyGeminiProModel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(CliProxy.Model, CliProxySettings.LegacyGeminiFlashModel, StringComparison.OrdinalIgnoreCase)))
                || (CliProxySettings.IsCodexProvider(CliProxy.Provider)
                    && (string.Equals(CliProxy.Model, CliProxySettings.DefaultGeminiModel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(CliProxy.Model, CliProxySettings.LegacyGeminiProModel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(CliProxy.Model, CliProxySettings.LegacyGeminiFlashModel, StringComparison.OrdinalIgnoreCase))))
            {
                CliProxy.Model = CliProxySettings.IsGeminiProvider(CliProxy.Provider)
                    ? CliProxySettings.DefaultGeminiModel
                    : CliProxySettings.DefaultCodexModel;
            }
        }

        private static bool IsLocalHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
        }

        private void SyncCliProxyModelEntry()
        {
            if (ModelPriority == null)
                ModelPriority = new List<ModelEntry>();

            ModelPriority = ModelPriority
                .Where(entry => !IsGeneratedCliProxyModelEntry(entry))
                .ToList();

            if (CliProxy?.Enabled != true)
                return;

            string baseUrl = CliProxy.GetApiBaseUrl();
            string apiKey = CliProxy.ApiKey;

            ModelPriority.Insert(0, new ModelEntry
            {
                Name = CliProxySettings.GeminiModelEntryName,
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                Model = CliProxySettings.DefaultGeminiModel
            });

            ModelPriority.Insert(0, new ModelEntry
            {
                Name = CliProxySettings.GeneratedModelEntryName,
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                Model = CliProxySettings.DefaultCodexModel
            });
        }

        private static bool IsGeneratedCliProxyModelEntry(ModelEntry entry)
        {
            return entry != null
                && !string.IsNullOrWhiteSpace(entry.Name)
                && (entry.Name.Equals(CliProxySettings.GeneratedModelEntryName, StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Equals(CliProxySettings.GeminiModelEntryName, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (!HasConfiguredChatTarget())
            {
                errors.Add("At least one entry must be added to the Model Priority list.");
            }

            if (ModelPriority != null)
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
                // OcrProvider.WindowsOcr: no required fields (local, offline)
            }

            if (CliProxy?.Enabled == true)
            {
                if (!IsLocalHost(CliProxy.Host))
                    errors.Add("CLI proxy host must stay local (127.0.0.1, localhost, or ::1).");

                if (CliProxy.Port <= 0 || CliProxy.Port > 65535)
                    errors.Add("CLI proxy port must be between 1 and 65535.");

                if (CliProxy.OAuthCallbackPort <= 0 || CliProxy.OAuthCallbackPort > 65535)
                    errors.Add("CLI proxy OAuth callback port must be between 1 and 65535.");

                if (string.IsNullOrWhiteSpace(CliProxy.Model))
                    errors.Add("CLI proxy model is required when CLI proxy is enabled.");

                if (!CliProxySettings.IsCodexProvider(CliProxy.Provider)
                    && !CliProxySettings.IsGeminiProvider(CliProxy.Provider))
                    errors.Add("CLI proxy provider must be Codex or Gemini.");

                if (string.IsNullOrWhiteSpace(CliProxy.ApiKey))
                    errors.Add("CLI proxy API key is required when CLI proxy is enabled.");

                if (string.IsNullOrWhiteSpace(CliProxy.ManagementKey))
                    errors.Add("CLI proxy management key is required when CLI proxy is enabled.");
            }

            if (Cdp?.Enabled == true)
            {
                if (Cdp.Port <= 0 || Cdp.Port > 65535)
                    errors.Add("CDP port must be between 1 and 65535.");

                if (Cdp.RetryIntervalMs < 1 || Cdp.RetryIntervalMs > 177013)
                    errors.Add("CDP retry interval must be between 1 and 177013 ms.");

                if (Cdp.ConnectTimeoutMs < 1 || Cdp.ConnectTimeoutMs > 177013)
                    errors.Add("CDP connect timeout must be between 1 and 177013 ms.");
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
