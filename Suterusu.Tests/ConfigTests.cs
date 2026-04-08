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
    }
}
