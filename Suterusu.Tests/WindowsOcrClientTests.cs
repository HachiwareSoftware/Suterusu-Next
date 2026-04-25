using System;
using System.Collections.Generic;
using Suterusu.Services;
using Xunit;

namespace Suterusu.Tests
{
    public class WindowsOcrClientTests
    {
        // ── Constructor ──────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_NullLanguageTag_DoesNotThrow()
        {
            // null is treated as empty string internally
            var _ = new WindowsOcrClient(new StubLogger(), null);
        }

        [Fact]
        public void Constructor_WhitespaceLanguageTag_DoesNotThrow()
        {
            var _ = new WindowsOcrClient(new StubLogger(), "   ");
        }

        [Fact]
        public void Constructor_ValidTag_DoesNotThrow()
        {
            var _ = new WindowsOcrClient(new StubLogger(), "en-US");
        }

        // ── GetAvailableLanguageTags ─────────────────────────────────────────────

        [Fact]
        public void GetAvailableLanguageTags_ReturnsNonNull()
        {
            // May return an empty list if no OCR packs installed, but must not be null.
            var tags = WindowsOcrClient.GetAvailableLanguageTags();
            Assert.NotNull(tags);
        }

        [Fact]
        public void GetAvailableLanguageTags_AllTagsAreNonEmpty()
        {
            var tags = WindowsOcrClient.GetAvailableLanguageTags();
            foreach (var tag in tags)
                Assert.False(string.IsNullOrWhiteSpace(tag), $"Language tag was blank: '{tag}'");
        }

        // ── GetLanguageDisplayName ───────────────────────────────────────────────

        [Fact]
        public void GetLanguageDisplayName_ValidTag_ReturnsNonEmpty()
        {
            string display = WindowsOcrClient.GetLanguageDisplayName("en-US");
            Assert.False(string.IsNullOrEmpty(display));
        }

        [Fact]
        public void GetLanguageDisplayName_InvalidTag_ReturnsNonNull()
        {
            // Fallback returns the tag itself on exception — must not throw or return null.
            string display = WindowsOcrClient.GetLanguageDisplayName("not-a-real-language-xyz");
            Assert.NotNull(display);
        }

        [Fact]
        public void GetLanguageDisplayName_EmptyTag_ReturnsNonNull()
        {
            string display = WindowsOcrClient.GetLanguageDisplayName(string.Empty);
            Assert.NotNull(display);
        }

        // ── Availability helpers ───────────────────────────────────────────────

        [Fact]
        public void CreateAvailability_ExplicitMissingLanguage_ReturnsValidationError()
        {
            WindowsOcrAvailability availability = WindowsOcrClient.CreateAvailability(
                "vi-VN",
                new[] { "en-US" });

            Assert.False(availability.IsRequestedLanguageAvailable);
            Assert.Contains("Language.Basic~~~vi-VN~0.0.1.0", availability.BuildConfigurationValidationError());
            Assert.Contains("Language.OCR~~~vi-VN~0.0.1.0", availability.BuildConfigurationValidationError());
        }

        [Fact]
        public void CreateAvailability_AutoWithoutVietnamese_WarnsAboutVietnamese()
        {
            WindowsOcrAvailability availability = WindowsOcrClient.CreateAvailability(
                string.Empty,
                new List<string> { "en-US", "fr-FR" });

            Assert.True(availability.HasSettingsWarning);
            Assert.Contains("Vietnamese OCR (vi-VN) is not installed", availability.BuildSettingsStatusMessage());
        }

        [Fact]
        public void CreateAvailability_AutoWithVietnamese_DoesNotWarn()
        {
            WindowsOcrAvailability availability = WindowsOcrClient.CreateAvailability(
                string.Empty,
                new[] { "en-US", "vi-VN" });

            Assert.False(availability.HasSettingsWarning);
            Assert.Contains("Auto uses your Windows profile languages", availability.BuildSettingsStatusMessage());
            Assert.Null(availability.BuildConfigurationValidationError());
        }

        [Fact]
        public void CreateAvailability_NoLanguages_ReturnsRecognizerInstallError()
        {
            WindowsOcrAvailability availability = WindowsOcrClient.CreateAvailability(
                string.Empty,
                Array.Empty<string>());

            Assert.True(availability.HasSettingsWarning);
            Assert.Contains("recognizers are not installed", availability.BuildConfigurationValidationError());
        }

        // ── Stub ─────────────────────────────────────────────────────────────────

        private sealed class StubLogger : ILogger
        {
            public void Debug(string message) { }
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception ex = null) { }
        }
    }
}
