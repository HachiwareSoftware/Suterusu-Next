using System.Collections.Generic;
using Xunit;
using Suterusu.Configuration;

namespace Suterusu.Tests
{
    public class ConfigTests
    {
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
        public void CreateDefault_ApiBaseUrl_ContainsOpenAi()
        {
            var config = AppConfig.CreateDefault();
            Assert.Contains("openai", config.ApiBaseUrl.ToLowerInvariant());
        }

        [Fact]
        public void CreateDefault_Models_NotEmpty()
        {
            var config = AppConfig.CreateDefault();
            Assert.NotNull(config.Models);
            Assert.NotEmpty(config.Models);
        }

        [Fact]
        public void CreateDefault_HistoryLimit_GreaterThanZero()
        {
            var config = AppConfig.CreateDefault();
            Assert.True(config.HistoryLimit > 0);
        }

        // -----------------------------------------------------------------------
        // Normalize — ApiBaseUrl
        // -----------------------------------------------------------------------

        [Fact]
        public void Normalize_TrimsTrailingSlash_FromApiBaseUrl()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com/",
                Models       = new List<string> { "gpt-4o-mini" },
                HistoryLimit = 10
            };

            config.Normalize();

            Assert.Equal("https://api.openai.com", config.ApiBaseUrl);
        }

        [Fact]
        public void Normalize_TripsMultipleTrailingSlashes_FromApiBaseUrl()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com///",
                Models       = new List<string> { "gpt-4o-mini" },
                HistoryLimit = 10
            };

            config.Normalize();

            Assert.Equal("https://api.openai.com", config.ApiBaseUrl);
        }

        [Fact]
        public void Normalize_UsesDefaultApiBaseUrl_WhenBlank()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "   ",
                Models       = new List<string> { "gpt-4o-mini" },
                HistoryLimit = 10
            };

            config.Normalize();

            Assert.False(string.IsNullOrWhiteSpace(config.ApiBaseUrl));
        }

        // -----------------------------------------------------------------------
        // Normalize — Models deduplication and blank removal
        // -----------------------------------------------------------------------

        [Fact]
        public void Normalize_RemovesDuplicateModels()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com",
                Models       = new List<string> { "gpt-4o-mini", "gpt-4o-mini", "gpt-4" },
                HistoryLimit = 10
            };

            config.Normalize();

            Assert.Equal(2, config.Models.Count);
        }

        [Fact]
        public void Normalize_RemovesBlankModels()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com",
                Models       = new List<string> { "gpt-4o-mini", "", "  ", null },
                HistoryLimit = 10
            };

            config.Normalize();

            Assert.Single(config.Models);
            Assert.Equal("gpt-4o-mini", config.Models[0]);
        }

        [Fact]
        public void Normalize_AddsDefaultModel_WhenModelsEmptyAfterCleaning()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com",
                Models       = new List<string> { "", "  " },
                HistoryLimit = 10
            };

            config.Normalize();

            Assert.NotEmpty(config.Models);
        }

        [Fact]
        public void Normalize_AddsDefaultModel_WhenModelsListIsNull()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com",
                Models       = null,
                HistoryLimit = 10
            };

            config.Normalize();

            Assert.NotNull(config.Models);
            Assert.NotEmpty(config.Models);
        }

        // -----------------------------------------------------------------------
        // Normalize — HistoryLimit clamping
        // -----------------------------------------------------------------------

        [Fact]
        public void Normalize_ClampsHistoryLimit_WhenNegative()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com",
                Models       = new List<string> { "gpt-4o-mini" },
                HistoryLimit = -5
            };

            config.Normalize();

            Assert.Equal(0, config.HistoryLimit);
        }

        [Fact]
        public void Normalize_ClampsHistoryLimit_WhenAbove100()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com",
                Models       = new List<string> { "gpt-4o-mini" },
                HistoryLimit = 200
            };

            config.Normalize();

            Assert.Equal(100, config.HistoryLimit);
        }

        [Fact]
        public void Normalize_PreservesHistoryLimit_WhenInRange()
        {
            var config = new AppConfig
            {
                ApiBaseUrl   = "https://api.openai.com",
                Models       = new List<string> { "gpt-4o-mini" },
                HistoryLimit = 42
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
        public void Validate_ReturnsNoErrors_ForValidDefaultConfig()
        {
            var config = AppConfig.CreateDefault().Normalize();
            var errors = config.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_ReturnsError_WhenApiBaseUrlIsEmpty()
        {
            var config = new AppConfig
            {
                ApiBaseUrl = "",
                Models     = new List<string> { "gpt-4o-mini" }
            };

            var errors = config.Validate();

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validate_ReturnsError_WhenApiBaseUrlIsWhitespace()
        {
            var config = new AppConfig
            {
                ApiBaseUrl = "   ",
                Models     = new List<string> { "gpt-4o-mini" }
            };

            var errors = config.Validate();

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validate_ReturnsError_WhenModelsListIsEmpty()
        {
            var config = new AppConfig
            {
                ApiBaseUrl = "https://api.openai.com",
                Models     = new List<string>()
            };

            var errors = config.Validate();

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validate_ReturnsError_WhenModelsListIsNull()
        {
            var config = new AppConfig
            {
                ApiBaseUrl = "https://api.openai.com",
                Models     = null
            };

            var errors = config.Validate();

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Validate_ReturnsMultipleErrors_WhenBothApiBaseUrlAndModelsInvalid()
        {
            var config = new AppConfig
            {
                ApiBaseUrl = "",
                Models     = null
            };

            var errors = config.Validate();

            Assert.True(errors.Count >= 2);
        }
    }
}
