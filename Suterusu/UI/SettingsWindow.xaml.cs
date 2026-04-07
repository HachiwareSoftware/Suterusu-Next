using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Suterusu.Configuration;
using Suterusu.Models;
using Suterusu.Services;

namespace Suterusu.UI
{
    /// <summary>
    /// Simple XAML-based settings window with a flat dark layout.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly ConfigManager         _configManager;
        private readonly ClipboardAiController _controller;
        private readonly ILogger               _logger = new NLogLogger("Suterusu.Settings");

        private bool _isApplyingPreset;
        private bool _isSyncingPresetSelection;

        public SettingsWindow(ConfigManager configManager, ClipboardAiController controller)
        {
            _configManager = configManager;
            _controller    = controller;

            EnsureWpfApplication();
            InitializeComponent();

            CboEndpointPresets.ItemsSource = EndpointPreset.GetPresets();
            RbFlashWindow.IsChecked = true;

            LoadConfig(_configManager.Current);
        }

        public void LoadConfig(AppConfig config)
        {
            if (config == null)
                return;

            TxtBaseUrl.Text        = config.ApiBaseUrl ?? string.Empty;
            PwdApiKey.Password     = config.ApiKey     ?? string.Empty;

            LstModels.Items.Clear();
            if (config.Models != null)
            {
                foreach (string model in config.Models)
                    LstModels.Items.Add(model);
            }

            TxtSystemPrompt.Text     = config.SystemPrompt ?? string.Empty;
            TxtHistoryLimit.Text     = config.HistoryLimit.ToString();
            TxtFlashWindowTarget.Text = config.FlashWindowTarget ?? "Chrome";
            SetNotificationMode(config.NotificationMode);
            MatchPresetFromBaseUrl(config.ApiBaseUrl);
            HideValidation();
        }

        private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            EndpointPreset preset = CboEndpointPresets.SelectedItem as EndpointPreset;
            if (preset == null)
                return;

            if (_isSyncingPresetSelection)
                return;

            _isApplyingPreset = true;
            TxtBaseUrl.Text = preset.BaseUrl;

            if (LstModels.Items.Count == 0 && !string.IsNullOrWhiteSpace(preset.DefaultModel))
                LstModels.Items.Add(preset.DefaultModel);

            _isApplyingPreset = false;
        }

        private void OnBaseUrlChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingPreset)
                return;

            EndpointPreset customPreset = CboEndpointPresets.Items
                .OfType<EndpointPreset>()
                .FirstOrDefault(p => p.Name == "Custom");

            if (customPreset != null && !ReferenceEquals(CboEndpointPresets.SelectedItem, customPreset))
            {
                _isSyncingPresetSelection = true;
                CboEndpointPresets.SelectedItem = customPreset;
                _isSyncingPresetSelection = false;
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (TrySave())
                Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnNewModelKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnAddModel(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void OnAddModel(object sender, RoutedEventArgs e)
        {
            string model = TxtNewModel.Text.Trim();
            if (string.IsNullOrWhiteSpace(model))
                return;

            if (!LstModels.Items.Contains(model))
                LstModels.Items.Add(model);

            TxtNewModel.Clear();
            TxtNewModel.Focus();
        }

        private void OnMoveModelUp(object sender, RoutedEventArgs e)
        {
            int index = LstModels.SelectedIndex;
            if (index <= 0)
                return;

            object item = LstModels.Items[index];
            LstModels.Items.RemoveAt(index);
            LstModels.Items.Insert(index - 1, item);
            LstModels.SelectedIndex = index - 1;
        }

        private void OnMoveModelDown(object sender, RoutedEventArgs e)
        {
            int index = LstModels.SelectedIndex;
            if (index < 0 || index >= LstModels.Items.Count - 1)
                return;

            object item = LstModels.Items[index];
            LstModels.Items.RemoveAt(index);
            LstModels.Items.Insert(index + 1, item);
            LstModels.SelectedIndex = index + 1;
        }

        private void OnRemoveModel(object sender, RoutedEventArgs e)
        {
            if (LstModels.SelectedItem == null)
                return;

            int index = LstModels.SelectedIndex;
            LstModels.Items.Remove(LstModels.SelectedItem);

            if (LstModels.Items.Count == 0)
                return;

            LstModels.SelectedIndex = index >= LstModels.Items.Count
                ? LstModels.Items.Count - 1
                : index;
        }

        private AppConfig BuildConfigFromInputs()
        {
            int historyLimit;
            if (!int.TryParse(TxtHistoryLimit.Text, out historyLimit))
                historyLimit = 10;

            return new AppConfig
            {
                ApiBaseUrl            = TxtBaseUrl.Text.Trim(),
                ApiKey                = PwdApiKey.Password,
                Models                = LstModels.Items.Cast<string>().ToList(),
                SystemPrompt          = TxtSystemPrompt.Text,
                HistoryLimit          = historyLimit,
                NotificationMode      = GetNotificationMode(),
                FlashWindowTarget     = TxtFlashWindowTarget.Text.Trim(),
                FlashWindowDurationMs = _configManager.Current.FlashWindowDurationMs
            };
        }

        private bool TrySave()
        {
            AppConfig config = BuildConfigFromInputs();
            var errors = config.Normalize().Validate();

            if (errors.Count > 0)
            {
                ShowValidation(string.Join(Environment.NewLine, errors));
                return false;
            }

            SaveConfigResult result = _configManager.Save(config);
            if (!result.Success)
            {
                ShowValidation(result.Error);
                return false;
            }

            _controller.RefreshConfiguration();
            _logger.Info("Settings saved and configuration refreshed.");
            HideValidation();
            return true;
        }

        private void MatchPresetFromBaseUrl(string baseUrl)
        {
            EndpointPreset matchingPreset = null;

            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                string normalizedUrl = baseUrl.TrimEnd('/');
                matchingPreset = CboEndpointPresets.Items
                    .OfType<EndpointPreset>()
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BaseUrl)
                        && p.BaseUrl.TrimEnd('/').Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase));
            }

            if (matchingPreset == null)
            {
                matchingPreset = CboEndpointPresets.Items
                    .OfType<EndpointPreset>()
                    .FirstOrDefault(p => p.Name == "Custom");
            }

            if (matchingPreset == null)
                return;

            _isApplyingPreset = true;
            CboEndpointPresets.SelectedItem = matchingPreset;
            _isApplyingPreset = false;
        }

        private NotificationMode GetNotificationMode()
        {
            if (RbCircleDot.IsChecked == true) return NotificationMode.CircleDot;
            if (RbNothing.IsChecked   == true) return NotificationMode.Nothing;
            return NotificationMode.FlashWindow;
        }

        private void SetNotificationMode(NotificationMode mode)
        {
            RbFlashWindow.IsChecked = mode == NotificationMode.FlashWindow;
            RbCircleDot.IsChecked   = mode == NotificationMode.CircleDot;
            RbNothing.IsChecked     = mode == NotificationMode.Nothing;
            UpdateFlashWindowSettingsVisibility(mode);
        }

        private void OnNotificationModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateFlashWindowSettingsVisibility(GetNotificationMode());
        }

        private void UpdateFlashWindowSettingsVisibility(NotificationMode mode)
        {
            if (PnlFlashWindowSettings == null)
                return;
            PnlFlashWindowSettings.Visibility =
                mode == NotificationMode.FlashWindow ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowValidation(string message)
        {
            LblValidationError.Text          = message;
            ValidationErrorBar.Visibility    = Visibility.Visible;
        }

        private void HideValidation()
        {
            ValidationErrorBar.Visibility    = Visibility.Collapsed;
            LblValidationError.Text          = string.Empty;
        }

        private void EnsureWpfApplication()
        {
            if (System.Windows.Application.Current == null)
                new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }
    }
}
