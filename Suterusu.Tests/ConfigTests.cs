using System.Collections.Generic;
using System.IO;
using Xunit;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Tests
{
    public class ConfigTests
    {
        private static ModelEntry ValidEntry(string url = "https://api.openai.com/v1", string model = "gpt-5.4-mini") =>
            new ModelEntry { Name = "Test", BaseUrl = url, ApiKey = "", Model = model };

        // -----------------------------------------------------------------------
        // CreateDefault
        // -----------------------------------------------------------------------

        [Fact]
        public void CreateDefault_ReturnsNonNull()
        {
            var config = AppConfig.CreateDefault();
            Assert.NotNull(config);
        }

        [Fact]
        public void CreateDefault_ModelPriority_IsNotNull()
        {
            var config = AppConfig.CreateDefault();
            Assert.NotNull(config.ModelPriority);
        }

        [Fact]
        public void CreateDefault_HistoryLimit_GreaterThanZero()
        {
            var config = AppConfig.CreateDefault();
            Assert.True(config.HistoryLimit > 0);
        }

        [Fact]
        public void CreateDefault_SystemPrompt_IsNotBlank()
        {
            var config = AppConfig.CreateDefault();
            Assert.False(string.IsNullOrWhiteSpace(config.SystemPrompt));
        }

        [Fact]
        public void CreateDefault_UsesExpectedHotkeyBindings()
        {
            var config = AppConfig.CreateDefault();

            Assert.Equal("F6", config.ClearHistoryHotkey);
            Assert.Equal("F7", config.SendClipboardHotkey);
            Assert.Equal("F8", config.CopyLastResponseHotkey);
            Assert.Equal("F12", config.QuitApplicationHotkey);
        }

        // -----------------------------------------------------------------------
        // Normalize — ModelPriority filtering
        // -----------------------------------------------------------------------

        [Fact]
        public void Normalize_InitializesNullModelPriority()
        {
            var config = new AppConfig { ModelPriority = null, HistoryLimit = 5 };
            config.Normalize();
            Assert.NotNull(config.ModelPriority);
        }

        [Fact]
        public void Normalize_RemovesEntriesWithBlankBaseUrl()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry>
                {
                    new ModelEntry { Name = "A", BaseUrl = "",    Model = "gpt-5.4-mini" },
                    new ModelEntry { Name = "B", BaseUrl = "https://api.openai.com", Model = "gpt-5.4-mini" }
                },
                HistoryLimit = 5
            };

            config.Normalize();

            Assert.Single(config.ModelPriority);
            Assert.Equal("B", config.ModelPriority[0].Name);
        }

        [Fact]
        public void Normalize_RemovesEntriesWithBlankModel()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry>
                {
                    new ModelEntry { Name = "A", BaseUrl = "https://api.openai.com", Model = "   " },
                    new ModelEntry { Name = "B", BaseUrl = "https://api.openai.com", Model = "gpt-5.4-mini" }
                },
                HistoryLimit = 5
            };

            config.Normalize();

            Assert.Single(config.ModelPriority);
            Assert.Equal("B", config.ModelPriority[0].Name);
        }

        [Fact]
        public void Normalize_PreservesValidEntries()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry>
                {
                    ValidEntry("https://api.openai.com", "gpt-5.4-mini"),
                    ValidEntry("https://openrouter.ai/api/v1", "mistral-7b")
                },
                HistoryLimit = 5
            };

            config.Normalize();

            Assert.Equal(2, config.ModelPriority.Count);
        }

        // -----------------------------------------------------------------------
        // Normalize — HistoryLimit clamping
        // -----------------------------------------------------------------------

        [Fact]
        public void Normalize_ClampsHistoryLimit_WhenNegative()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                HistoryLimit  = -5
            };

            config.Normalize();

            Assert.Equal(0, config.HistoryLimit);
        }

        [Fact]
        public void Normalize_ClampsHistoryLimit_WhenAbove100()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                HistoryLimit  = 200
            };

            config.Normalize();

            Assert.Equal(100, config.HistoryLimit);
        }

        [Fact]
        public void Normalize_PreservesHistoryLimit_WhenInRange()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                HistoryLimit  = 42
            };

            config.Normalize();

            Assert.Equal(42, config.HistoryLimit);
        }

        [Fact]
        public void Normalize_ReturnsThis_ForFluentChaining()
        {
            var config = AppConfig.CreateDefault();
            var returned = config.Normalize();
            Assert.Same(config, returned);
        }

        [Fact]
        public void Normalize_ReplacesInvalidHotkeys_WithDefaults()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                ClearHistoryHotkey = "bad-key",
                SendClipboardHotkey = "f13",
                CopyLastResponseHotkey = null,
                QuitApplicationHotkey = "   "
            };

            config.Normalize();

            Assert.Equal("F6", config.ClearHistoryHotkey);
            Assert.Equal("F13", config.SendClipboardHotkey);
            Assert.Equal("F8", config.CopyLastResponseHotkey);
            Assert.Equal("F12", config.QuitApplicationHotkey);
        }

        [Fact]
        public void Normalize_CanonicalizesValidKeyCombinations()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                ClearHistoryHotkey = "control + shift + k",
                SendClipboardHotkey = "win+v",
                CopyLastResponseHotkey = "alt + f9",
                QuitApplicationHotkey = "ctrl+alt+delete"
            };

            config.Normalize();

            Assert.Equal("Ctrl+Shift+K", config.ClearHistoryHotkey);
            Assert.Equal("Win+V", config.SendClipboardHotkey);
            Assert.Equal("Alt+F9", config.CopyLastResponseHotkey);
            Assert.Equal("Ctrl+Alt+DELETE", config.QuitApplicationHotkey);
        }

        [Fact]
        public void Normalize_ResetsDuplicateHotkeys_ToDefaults()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                ClearHistoryHotkey = "F9",
                SendClipboardHotkey = "F9",
                CopyLastResponseHotkey = "F10",
                QuitApplicationHotkey = "F11"
            };

            config.Normalize();

            Assert.Equal("F6", config.ClearHistoryHotkey);
            Assert.Equal("F7", config.SendClipboardHotkey);
            Assert.Equal("F8", config.CopyLastResponseHotkey);
            Assert.Equal("F12", config.QuitApplicationHotkey);
        }

        // -----------------------------------------------------------------------
        // Validate
        // -----------------------------------------------------------------------

        [Fact]
        public void Validate_ReturnsNoErrors_ForConfigWithValidEntry()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                HistoryLimit  = 10
            };
            config.Normalize();

            var errors = config.Validate();

            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_ReturnsError_WhenModelPriorityIsEmpty()
        {
            var config = new AppConfig { ModelPriority = new List<ModelEntry>() };
            var errors = config.Validate();
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validate_ReturnsError_WhenModelPriorityIsNull()
        {
            var config = new AppConfig { ModelPriority = null };
            var errors = config.Validate();
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validate_ReturnsError_WhenEntryHasBlankBaseUrl()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry>
                {
                    new ModelEntry { Name = "A", BaseUrl = "", Model = "gpt-5.4-mini" }
                }
            };

            var errors = config.Validate();

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validate_ReturnsError_WhenEntryHasBlankModel()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry>
                {
                    new ModelEntry { Name = "A", BaseUrl = "https://api.openai.com", Model = "" }
                }
            };

            var errors = config.Validate();

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validate_ReturnsMultipleErrors_ForMultipleInvalidEntries()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry>
                {
                    new ModelEntry { Name = "A", BaseUrl = "", Model = "" },
                    new ModelEntry { Name = "B", BaseUrl = "", Model = "" }
                }
            };

            var errors = config.Validate();

            Assert.True(errors.Count >= 2);
        }

        [Fact]
        public void Validate_ReturnsError_WhenHotkeysAreDuplicated()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                ClearHistoryHotkey = "Ctrl+K",
                SendClipboardHotkey = "control+k",
                CopyLastResponseHotkey = "F8",
                QuitApplicationHotkey = "F12"
            };

            var errors = config.Validate();

            Assert.Contains(errors, error => error.Contains("assigned more than once"));
        }

        [Fact]
        public void Validate_ReturnsError_WhenHotkeyIsOutsideSupportedRange()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                ClearHistoryHotkey = "Ctrl+Alt",
                SendClipboardHotkey = "F7",
                CopyLastResponseHotkey = "F8",
                QuitApplicationHotkey = "F12"
            };

            var errors = config.Validate();

            Assert.Contains(errors, error => error.Contains("Clear history hotkey"));
        }

        // -----------------------------------------------------------------------
        // CLI proxy settings
        // -----------------------------------------------------------------------

        [Fact]
        public void CreateDefault_InitializesCliProxyDefaults()
        {
            var config = AppConfig.CreateDefault();

            Assert.NotNull(config.CliProxy);
            Assert.Equal("127.0.0.1", config.CliProxy.Host);
            Assert.Equal(8317, config.CliProxy.Port);
            Assert.Equal(CliProxySettings.CodexProvider, config.CliProxy.Provider);
            Assert.False(string.IsNullOrWhiteSpace(config.CliProxy.ApiKey));
            Assert.False(string.IsNullOrWhiteSpace(config.CliProxy.ManagementKey));
            Assert.Equal(CliProxySettings.DefaultCodexModel, config.CliProxy.Model);
        }

        [Fact]
        public void Normalize_InitializesAndClampsCliProxyValues()
        {
            var config = AppConfig.CreateDefault();
            config.CliProxy = new CliProxySettings
            {
                Host = "0.0.0.0",
                Port = 0,
                OAuthCallbackPort = 70000,
                ApiKey = "",
                ManagementKey = ""
            };

            config.Normalize();

            Assert.Equal("127.0.0.1", config.CliProxy.Host);
            Assert.Equal(8317, config.CliProxy.Port);
            Assert.Equal(1455, config.CliProxy.OAuthCallbackPort);
            Assert.False(string.IsNullOrWhiteSpace(config.CliProxy.ApiKey));
            Assert.False(string.IsNullOrWhiteSpace(config.CliProxy.ManagementKey));
        }

        [Fact]
        public void Normalize_MigratesLegacyCliProxyPathsToLocalRuntimeDirectory()
        {
            var config = AppConfig.CreateDefault();
            string legacyRuntime = CliProxySettings.GetLegacyRuntimeDirectory();
            config.CliProxy.RuntimeDirectory = legacyRuntime;
            config.CliProxy.ExecutablePath = CliProxySettings.GetExecutablePath(legacyRuntime);
            config.CliProxy.ConfigPath = Path.Combine(legacyRuntime, "config.yaml");
            config.CliProxy.AuthDirectory = Path.Combine(legacyRuntime, "auths");

            config.Normalize();

            string expectedRuntime = CliProxySettings.GetDefaultRuntimeDirectory();
            Assert.Equal(expectedRuntime, config.CliProxy.RuntimeDirectory);
            Assert.Equal(CliProxySettings.GetExecutablePath(expectedRuntime), config.CliProxy.ExecutablePath);
            Assert.Equal(Path.Combine(expectedRuntime, "config.yaml"), config.CliProxy.ConfigPath);
            Assert.Equal(Path.Combine(expectedRuntime, "auths"), config.CliProxy.AuthDirectory);
        }

        [Fact]
        public void Normalize_RegeneratesManagementKey_WhenKeysMatch()
        {
            var config = AppConfig.CreateDefault();
            config.CliProxy.ApiKey = "same-key";
            config.CliProxy.ManagementKey = "same-key";

            config.Normalize();

            Assert.NotEqual(config.CliProxy.ApiKey, config.CliProxy.ManagementKey);
        }

        [Fact]
        public void Normalize_AddsCliProxyModelEntry_WhenCliProxyEnabled()
        {
            var config = AppConfig.CreateDefault();
            config.ModelPriority = new List<ModelEntry>();
            config.CliProxy.Enabled = true;
            config.CliProxy.Host = "127.0.0.1";
            config.CliProxy.Port = 8317;
            config.CliProxy.Model = "gpt-5.3-codex";
            config.CliProxy.ApiKey = "secret";

            config.Normalize();

            Assert.Equal(2, config.ModelPriority.Count);
            Assert.Contains(config.ModelPriority, entry =>
                entry.Name == CliProxySettings.GeneratedModelEntryName
                && entry.BaseUrl == "http://127.0.0.1:8317/v1"
                && entry.Model == CliProxySettings.DefaultCodexModel
                && entry.ApiKey == "secret");
            Assert.Contains(config.ModelPriority, entry =>
                entry.Name == CliProxySettings.GeminiModelEntryName
                && entry.BaseUrl == "http://127.0.0.1:8317/v1"
                && entry.Model == CliProxySettings.DefaultGeminiModel
                && entry.ApiKey == "secret");
        }

        [Fact]
        public void Normalize_GeminiProviderControlsLoginModel_NotGeneratedEntries()
        {
            var config = AppConfig.CreateDefault();
            config.ModelPriority = new List<ModelEntry>();
            config.CliProxy.Enabled = true;
            config.CliProxy.Provider = CliProxySettings.GeminiProvider;
            config.CliProxy.Model = "gpt-5.3-codex";
            config.CliProxy.ApiKey = "secret";

            config.Normalize();

            Assert.Equal(CliProxySettings.DefaultGeminiModel, config.CliProxy.Model);
            Assert.Equal(2, config.ModelPriority.Count);
            Assert.Contains(config.ModelPriority, entry => entry.Name == CliProxySettings.GeneratedModelEntryName);
            Assert.Contains(config.ModelPriority, entry => entry.Name == CliProxySettings.GeminiModelEntryName);
        }

        [Fact]
        public void Normalize_SwitchesBackToCodexDefault_WhenProviderIsCodex()
        {
            var config = AppConfig.CreateDefault();
            config.CliProxy.Provider = CliProxySettings.CodexProvider;
            config.CliProxy.Model = CliProxySettings.LegacyGeminiProModel;

            config.Normalize();

            Assert.Equal(CliProxySettings.DefaultCodexModel, config.CliProxy.Model);
        }

        [Fact]
        public void Normalize_RemovesCliProxyModelEntry_WhenCliProxyDisabled()
        {
            var config = AppConfig.CreateDefault();
            config.ModelPriority = new List<ModelEntry>
            {
                new ModelEntry
                {
                    Name = CliProxySettings.GeneratedModelEntryName,
                    BaseUrl = "http://127.0.0.1:8317/v1",
                    ApiKey = "secret",
                    Model = "gpt-5.3-codex"
                },
                new ModelEntry
                {
                    Name = CliProxySettings.GeminiModelEntryName,
                    BaseUrl = "http://127.0.0.1:8317/v1",
                    ApiKey = "secret",
                    Model = CliProxySettings.LegacyGeminiProModel
                },
                ValidEntry("https://api.openai.com/v1", "gpt-5.4-mini")
            };

            config.Normalize();

            var entry = Assert.Single(config.ModelPriority);
            Assert.Equal("Test", entry.Name);
        }

        [Fact]
        public void Validate_AllowsCliProxyWithoutManualModelPriority()
        {
            var config = AppConfig.CreateDefault();
            config.ModelPriority = new List<ModelEntry>();
            config.CliProxy.Enabled = true;

            var errors = config.Validate();

            Assert.DoesNotContain(errors, error => error.Contains("Model Priority list"));
        }

        [Fact]
        public void Normalize_UsesBracketedIpv6LoopbackInCliProxyModelEntry()
        {
            var config = AppConfig.CreateDefault();
            config.ModelPriority = new List<ModelEntry>();
            config.CliProxy.Enabled = true;
            config.CliProxy.Host = "::1";

            config.Normalize();

            Assert.Equal(2, config.ModelPriority.Count);
            Assert.All(config.ModelPriority, entry => Assert.Equal("http://[::1]:8317/v1", entry.BaseUrl));
        }

        [Fact]
        public void Validate_ReturnsError_WhenCliProxyHostIsNotLocal()
        {
            var config = AppConfig.CreateDefault();
            config.ModelPriority = new List<ModelEntry> { ValidEntry() };
            config.CliProxy.Enabled = true;
            config.CliProxy.Host = "0.0.0.0";

            var errors = config.Validate();

            Assert.Contains(errors, error => error.Contains("must stay local"));
        }

        [Fact]
        public void Validate_ReturnsError_WhenCliProxyPortsAreInvalid()
        {
            var config = AppConfig.CreateDefault();
            config.ModelPriority = new List<ModelEntry> { ValidEntry() };
            config.CliProxy.Enabled = true;
            config.CliProxy.Port = 70000;
            config.CliProxy.OAuthCallbackPort = -1;

            var errors = config.Validate();

            Assert.Contains(errors, error => error.Contains("CLI proxy port"));
            Assert.Contains(errors, error => error.Contains("callback port"));
        }

        // -----------------------------------------------------------------------
        // Windows OCR — OcrSettings.WindowsOcrLanguage
        // -----------------------------------------------------------------------

        [Fact]
        public void OcrSettings_CreateDefault_WindowsOcrLanguage_IsEmpty()
        {
            var ocr = OcrSettings.CreateDefault();
            Assert.Equal(string.Empty, ocr.WindowsOcrLanguage);
        }

        [Fact]
        public void Normalize_Ocr_NullWindowsOcrLanguage_BecomesEmpty()
        {
            var config = AppConfig.CreateDefault();
            config.Ocr.WindowsOcrLanguage = null;
            config.Normalize();
            Assert.Equal(string.Empty, config.Ocr.WindowsOcrLanguage);
        }

        [Fact]
        public void Normalize_Ocr_WhitespaceWindowsOcrLanguage_BecomesEmpty()
        {
            var config = AppConfig.CreateDefault();
            config.Ocr.WindowsOcrLanguage = "   ";
            config.Normalize();
            Assert.Equal(string.Empty, config.Ocr.WindowsOcrLanguage);
        }

        [Fact]
        public void Normalize_Ocr_LanguageTagWithPadding_IsTrimmed()
        {
            var config = AppConfig.CreateDefault();
            config.Ocr.WindowsOcrLanguage = "  en-US  ";
            config.Normalize();
            Assert.Equal("en-US", config.Ocr.WindowsOcrLanguage);
        }

        [Fact]
        public void Validate_WindowsOcr_Enabled_NoRequiredFieldErrors()
        {
            // WindowsOcr is local / offline — no URL or token required.
            var config = new AppConfig
            {
                ModelPriority  = new List<ModelEntry> { ValidEntry() },
                Ocr = new OcrSettings
                {
                    Enabled             = true,
                    Provider            = OcrProvider.WindowsOcr,
                    WindowsOcrLanguage  = "",
                    Hotkey              = "Shift+F7",
                    TimeoutMs           = 30000
                },
                ClearHistoryHotkey     = "F6",
                SendClipboardHotkey    = "F7",
                CopyLastResponseHotkey = "F8",
                QuitApplicationHotkey  = "F12"
            };

            var errors = config.Validate();

            Assert.DoesNotContain(errors, e =>
                e.Contains("URL") || e.Contains("token") || e.Contains("API key") || e.Contains("model"));
        }

        [Fact]
        public void Validate_WindowsAiOcr_Enabled_NoRequiredFieldErrors()
        {
            var config = new AppConfig
            {
                ModelPriority  = new List<ModelEntry> { ValidEntry() },
                Ocr = new OcrSettings
                {
                    Enabled   = true,
                    Provider  = OcrProvider.WindowsAi,
                    Hotkey    = "Shift+F7",
                    TimeoutMs = 30000
                },
                ClearHistoryHotkey     = "F6",
                SendClipboardHotkey    = "F7",
                CopyLastResponseHotkey = "F8",
                QuitApplicationHotkey  = "F12"
            };

            var errors = config.Validate();

            Assert.DoesNotContain(errors, e =>
                e.Contains("URL") || e.Contains("token") || e.Contains("API key") || e.Contains("model"));
        }

        [Fact]
        public void OcrSettings_CreateDefault_PaddleXUrl_UsesLocalServer()
        {
            var ocr = OcrSettings.CreateDefault();
            Assert.Equal("http://localhost:8080", ocr.PaddleXUrl);
        }

        [Fact]
        public void Normalize_Ocr_BlankPaddleXUrl_UsesDefault()
        {
            var config = AppConfig.CreateDefault();
            config.Ocr.PaddleXUrl = "   ";

            config.Normalize();

            Assert.Equal("http://localhost:8080", config.Ocr.PaddleXUrl);
        }

        [Fact]
        public void Validate_PaddleX_Enabled_RequiresUrl()
        {
            var config = new AppConfig
            {
                ModelPriority = new List<ModelEntry> { ValidEntry() },
                Ocr = new OcrSettings
                {
                    Enabled = true,
                    Provider = OcrProvider.PaddleX,
                    PaddleXUrl = "",
                    Hotkey = "Shift+F7",
                    TimeoutMs = 30000
                },
                ClearHistoryHotkey = "F6",
                SendClipboardHotkey = "F7",
                CopyLastResponseHotkey = "F8",
                QuitApplicationHotkey = "F12"
            };

            var errors = config.Validate();

            Assert.Contains(errors, e => e.Contains("PaddleX URL"));
        }

        // -----------------------------------------------------------------------
        // CDP settings
        // -----------------------------------------------------------------------

        [Fact]
        public void CdpSettings_CreateDefault_UsesSafeConnectOnlyDefaults()
        {
            var cdp = CdpSettings.CreateDefault();

            Assert.False(cdp.Enabled);
            Assert.Equal(27245, cdp.Port);
            Assert.Equal(string.Empty, cdp.UrlPattern);
            Assert.Equal("js/events", cdp.StartupScriptsDirectory);
            Assert.Equal(5000, cdp.RetryIntervalMs);
            Assert.Equal(2000, cdp.ConnectTimeoutMs);
            Assert.True(cdp.InjectOnStartup);
        }

        [Fact]
        public void Normalize_Cdp_NullSettings_CreatesDefault()
        {
            var config = AppConfig.CreateDefault();
            config.Cdp = null;

            config.Normalize();

            Assert.NotNull(config.Cdp);
            Assert.Equal(27245, config.Cdp.Port);
        }

        [Fact]
        public void Normalize_Cdp_ClampsInvalidValues()
        {
            var config = AppConfig.CreateDefault();
            config.Cdp = new CdpSettings
            {
                Enabled = true,
                Port = 70000,
                UrlPattern = "  chatgpt\\.com  ",
                StartupScriptsDirectory = "   ",
                RetryIntervalMs = 0,
                ConnectTimeoutMs = 0,
                InjectOnStartup = true
            };

            config.Normalize();

            Assert.Equal(27245, config.Cdp.Port);
            Assert.Equal("chatgpt\\.com", config.Cdp.UrlPattern);
            Assert.Equal("js/events", config.Cdp.StartupScriptsDirectory);
            Assert.Equal(1, config.Cdp.RetryIntervalMs);
            Assert.Equal(1, config.Cdp.ConnectTimeoutMs);
        }

        [Fact]
        public void Normalize_Cdp_MigratesLegacyStartupDirectory()
        {
            var config = AppConfig.CreateDefault();
            config.Cdp.StartupScriptsDirectory = "js/startup";

            config.Normalize();

            Assert.Equal("js/events", config.Cdp.StartupScriptsDirectory);
        }

        [Fact]
        public void Normalize_Cdp_ClampsHighRetryAndTimeout()
        {
            var config = AppConfig.CreateDefault();
            config.Cdp.RetryIntervalMs = 200000;
            config.Cdp.ConnectTimeoutMs = 200000;

            config.Normalize();

            Assert.Equal(177013, config.Cdp.RetryIntervalMs);
            Assert.Equal(177013, config.Cdp.ConnectTimeoutMs);
        }
    }
}
