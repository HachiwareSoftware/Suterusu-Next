using System.Collections.Generic;
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
    }
}
