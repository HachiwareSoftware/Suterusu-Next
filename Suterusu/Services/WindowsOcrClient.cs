using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Suterusu.Models;

namespace Suterusu.Services
{
    public sealed class WindowsOcrAvailability
    {
        private readonly IReadOnlyList<string> _availableLanguageTags;

        public WindowsOcrAvailability(string requestedLanguageTag, IReadOnlyList<string> availableLanguageTags)
        {
            RequestedLanguageTag = (requestedLanguageTag ?? string.Empty).Trim();
            _availableLanguageTags = availableLanguageTags ?? Array.Empty<string>();
        }

        public string RequestedLanguageTag { get; }

        public IReadOnlyList<string> AvailableLanguageTags => _availableLanguageTags;

        public bool UsesUserProfileLanguages => string.IsNullOrEmpty(RequestedLanguageTag);

        public bool HasAnyRecognizerLanguages => _availableLanguageTags.Count > 0;

        public bool IsRequestedLanguageAvailable =>
            UsesUserProfileLanguages || HasLanguage(RequestedLanguageTag);

        public bool IsVietnameseAvailable => HasLanguage("vi-VN");

        public bool HasSettingsWarning =>
            !HasAnyRecognizerLanguages ||
            (!UsesUserProfileLanguages && !IsRequestedLanguageAvailable) ||
            (UsesUserProfileLanguages && !IsVietnameseAvailable);

        public bool HasLanguage(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
                return false;

            return _availableLanguageTags.Any(tag =>
                string.Equals(tag, languageTag.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public string BuildConfigurationValidationError()
        {
            return BuildBlockingMessage();
        }

        public string BuildEngineCreationFailureMessage()
        {
            return BuildBlockingMessage()
                ?? "Windows OCR could not create a recognizer for the current Windows language profile.";
        }

        public string BuildSettingsStatusMessage()
        {
            string blockingMessage = BuildBlockingMessage();
            if (blockingMessage != null)
                return blockingMessage;

            string installedTags = string.Join(", ", _availableLanguageTags);

            if (UsesUserProfileLanguages && !IsVietnameseAvailable)
            {
                return $"Installed Windows OCR languages: {installedTags}. Vietnamese OCR (vi-VN) is not installed, so Auto will not recognize Vietnamese correctly. {BuildInstallInstructions("vi-VN")}";
            }

            if (UsesUserProfileLanguages)
                return $"Installed Windows OCR languages: {installedTags}. Auto uses your Windows profile languages.";

            return $"Installed Windows OCR languages: {installedTags}.";
        }

        public string BuildMissingRequestedLanguageMessage()
        {
            return $"Windows OCR language '{RequestedLanguageTag}' is not installed. {BuildInstallInstructions(RequestedLanguageTag)}";
        }

        private string BuildNoRecognizerLanguagesMessage()
        {
            return "Windows OCR recognizers are not installed. Install a Windows language and its Optical character recognition feature, then reopen or refresh this dialog.";
        }

        private string BuildBlockingMessage()
        {
            if (!HasAnyRecognizerLanguages)
                return BuildNoRecognizerLanguagesMessage();

            if (!UsesUserProfileLanguages && !IsRequestedLanguageAvailable)
                return BuildMissingRequestedLanguageMessage();

            return null;
        }

        private static string BuildInstallInstructions(string languageTag)
        {
            return $"Install '{languageTag}' in Windows Settings and enable its OCR feature. If you manage capabilities directly, install Language.Basic~~~{languageTag}~0.0.1.0 and Language.OCR~~~{languageTag}~0.0.1.0.";
        }
    }

    /// <summary>
    /// IOcrClient implementation that uses the built-in Windows.Media.Ocr engine.
    /// Requires Windows 8.1 or later with at least one OCR language pack installed.
    /// Local / offline — no API key or network connection needed.
    /// The <paramref name="prompt"/> parameter is ignored; Windows OCR takes no prompt.
    /// </summary>
    public class WindowsOcrClient : IOcrClient
    {
        private readonly ILogger _logger;
        private readonly string _languageTag; // BCP-47, e.g. "en-US"; "" = auto

        public WindowsOcrClient(ILogger logger, string languageTag)
        {
            _logger      = logger;
            _languageTag = (languageTag ?? string.Empty).Trim();
        }

        /// <inheritdoc />
        public async Task<AiSingleAttemptResult> RunOcrAsync(
            byte[] imageData,
            string prompt,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(timeoutMs);
                var ct = cts.Token;

                try
                {
                    WindowsOcrAvailability availability = GetAvailability(_languageTag);

                    if (!availability.UsesUserProfileLanguages && !availability.IsRequestedLanguageAvailable)
                        return AiSingleAttemptResult.Fail(availability.BuildMissingRequestedLanguageMessage());

                    // 1. Select OCR engine based on configured language
                    OcrEngine engine = availability.UsesUserProfileLanguages
                        ? OcrEngine.TryCreateFromUserProfileLanguages()
                        : OcrEngine.TryCreateFromLanguage(new Language(_languageTag));

                    if (engine == null)
                        return AiSingleAttemptResult.Fail(availability.BuildEngineCreationFailureMessage());

                    _logger.Info($"Windows OCR recognizer language: {engine.RecognizerLanguage?.LanguageTag ?? "(unknown)"}");

                    // 2. Wrap PNG bytes in a random-access stream for WinRT decoders
                    using (var ms = new MemoryStream(imageData))
                    {
                        var rasStream = ms.AsRandomAccessStream();

                        // 3. Decode the image
                        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(rasStream).AsTask(ct);

                        // 4. Enforce Windows OCR's 2000 px dimension limit
                        if (decoder.PixelWidth > OcrEngine.MaxImageDimension ||
                            decoder.PixelHeight > OcrEngine.MaxImageDimension)
                        {
                            return AiSingleAttemptResult.Fail(
                                $"Captured region ({decoder.PixelWidth}×{decoder.PixelHeight} px) exceeds the " +
                                $"Windows OCR maximum of {OcrEngine.MaxImageDimension}×{OcrEngine.MaxImageDimension} px. " +
                                "Select a smaller region.");
                        }

                        // 5. Get SoftwareBitmap (Bgra8 premultiplied — accepted by OcrEngine)
                        using (SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(ct))
                        {
                            // 6. Run recognition
                            OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(ct);

                            if (result == null || result.Lines.Count == 0)
                                return AiSingleAttemptResult.Fail("No text recognized in the selected region.");

                            string text = string.Join("\n", result.Lines.Select(l => l.Text));
                            _logger.Info($"Windows OCR: recognized {result.Lines.Count} line(s).");
                            return AiSingleAttemptResult.Ok(text);
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return AiSingleAttemptResult.Fail("Windows OCR timed out.");
                }
                catch (OperationCanceledException)
                {
                    return AiSingleAttemptResult.Fail("Windows OCR was cancelled.");
                }
                catch (Exception ex)
                {
                    _logger.Error("Windows OCR failed.", ex);
                    return AiSingleAttemptResult.Fail($"Windows OCR failed: {ex.Message}");
                }
            }
        }

        // ── Settings helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns BCP-47 language tags for all installed Windows OCR recognizer languages.
        /// Throws <see cref="PlatformNotSupportedException"/> on Windows versions before 8.1.
        /// </summary>
        public static IReadOnlyList<string> GetAvailableLanguageTags()
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(l => l.LanguageTag)
                .ToList()
                .AsReadOnly();
        }

        public static WindowsOcrAvailability GetAvailability(string requestedLanguageTag)
        {
            return CreateAvailability(requestedLanguageTag, GetAvailableLanguageTags());
        }

        public static WindowsOcrAvailability CreateAvailability(string requestedLanguageTag, IEnumerable<string> availableLanguageTags)
        {
            var sanitizedTags = (availableLanguageTags ?? Enumerable.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();

            return new WindowsOcrAvailability(requestedLanguageTag, sanitizedTags);
        }

        /// <summary>Returns the human-readable display name for a BCP-47 language tag.</summary>
        public static string GetLanguageDisplayName(string tag)
        {
            try { return new Language(tag).DisplayName; }
            catch { return tag; }
        }
    }
}
