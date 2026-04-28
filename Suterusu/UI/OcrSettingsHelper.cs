using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Suterusu.Configuration;
using Suterusu.Services;

namespace Suterusu.UI
{
    internal class OcrSettingsHelper
    {
        private readonly ComboBox _languageCombo;
        private readonly TextBlock _statusText;
        private readonly Func<OcrProvider> _getOcrProvider;
        private readonly Action<string> _showValidation;
        private readonly Action<string> _updateStatus;
        private readonly ILogger _logger;
        private WindowsOcrAvailability _availability;

        public OcrSettingsHelper(
            ComboBox languageCombo,
            TextBlock statusText,
            Func<OcrProvider> getOcrProvider,
            Action<string> showValidation,
            Action<string> updateStatus,
            ILogger logger)
        {
            _languageCombo = languageCombo;
            _statusText = statusText;
            _getOcrProvider = getOcrProvider;
            _showValidation = showValidation;
            _updateStatus = updateStatus;
            _logger = logger;
            _availability = WindowsOcrClient.CreateAvailability(string.Empty, Array.Empty<string>());
        }

        public void PopulateLanguageDropdown(string selectedTag)
        {
            _languageCombo.Items.Clear();

            var autoItem = new ComboBoxItem { Content = "Auto (user profile)", Tag = string.Empty };
            _languageCombo.Items.Add(autoItem);

            try
            {
                _availability = WindowsOcrClient.GetAvailability(selectedTag);
                foreach (var tag in _availability.AvailableLanguageTags)
                {
                    string display = WindowsOcrClient.GetLanguageDisplayName(tag);
                    _languageCombo.Items.Add(new ComboBoxItem
                    {
                        Content = $"{display} \u2014 {tag}",
                        Tag = tag
                    });
                }
            }
            catch (Exception ex)
            {
                _availability = WindowsOcrClient.CreateAvailability(selectedTag, Array.Empty<string>());
                _logger.Warn($"Could not enumerate Windows OCR languages: {ex.Message}");
            }

            ComboBoxItem toSelect = autoItem;
            if (!string.IsNullOrEmpty(selectedTag))
            {
                foreach (ComboBoxItem item in _languageCombo.Items)
                {
                    if (string.Equals(item.Tag as string, selectedTag, StringComparison.OrdinalIgnoreCase))
                    {
                        toSelect = item;
                        break;
                    }
                }

                if (toSelect == autoItem && !_availability.IsRequestedLanguageAvailable)
                {
                    toSelect = new ComboBoxItem
                    {
                        Content = $"Missing from Windows: {selectedTag}",
                        Tag = selectedTag,
                        FontStyle = FontStyles.Italic
                    };
                    _languageCombo.Items.Add(toSelect);
                }
            }

            _languageCombo.SelectedItem = toSelect;
            UpdateStatus();
        }

        public string GetSelectedLanguageTag()
        {
            return (_languageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        }

        public WindowsOcrAvailability GetAvailability()
        {
            return _availability;
        }

        public string GetValidationError(AppConfig config)
        {
            if (config?.Ocr == null || config.Ocr.Provider != OcrProvider.WindowsOcr)
                return null;

            string selectedTag = config.Ocr.WindowsOcrLanguage;
            var availability = WindowsOcrClient.CreateAvailability(selectedTag, _availability.AvailableLanguageTags);
            return availability.BuildConfigurationValidationError();
        }

        public void UpdateStatus()
        {
            if (_getOcrProvider() != OcrProvider.WindowsOcr)
            {
                _statusText.Visibility = System.Windows.Visibility.Collapsed;
                _statusText.Text = string.Empty;
                return;
            }

            string selectedTag = GetSelectedLanguageTag();
            var availability = WindowsOcrClient.CreateAvailability(selectedTag, _availability.AvailableLanguageTags);
            string statusText = availability.BuildSettingsStatusMessage();

            _statusText.Text = statusText;
            _statusText.Foreground = (Brush)_statusText.TryFindResource(
                availability.HasSettingsWarning ? "ErrorBrush" : "MutedTextBrush");
            _statusText.Visibility = string.IsNullOrWhiteSpace(statusText)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        }

        public void Refresh()
        {
            PopulateLanguageDropdown(GetSelectedLanguageTag());
        }

        public string GetSelectedWindowsOcrLanguage()
        {
            return (_languageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        }
    }
}
