using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Suterusu.Configuration;
using Suterusu.Models;
using Suterusu.Services;

namespace Suterusu.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly ConfigManager         _configManager;
        private readonly ClipboardAiController _controller;
        private readonly ILogger               _logger = new NLogLogger("Suterusu.Settings");

        // -2 = not in edit mode, -1 = adding new entry, >= 0 = editing existing entry
        private int  _editingEntryIndex    = -2;
        private bool _isApplyingEntryPreset;
        private bool _isSyncingEntryPreset;

        public SettingsWindow(ConfigManager configManager, ClipboardAiController controller = null)
        {
            _configManager = configManager;
            _controller    = controller;

            InitializeComponent();

            CboEntryPreset.ItemsSource = EndpointPreset.GetPresets();
            RbFlashWindow.IsChecked    = true;
            RbSequential.IsChecked     = true;

            LoadConfig(_configManager.Current);
        }

        // ── Load / Save ───────────────────────────────────────────────────────

        public void LoadConfig(AppConfig config)
        {
            if (config == null)
                return;

            LstPriority.Items.Clear();
            if (config.ModelPriority != null)
            {
                foreach (var entry in config.ModelPriority)
                    LstPriority.Items.Add(entry.Clone());
            }

            TxtHistoryLimit.Text              = config.HistoryLimit.ToString();
            TxtFlashWindowTarget.Text         = config.FlashWindowTarget ?? "Chrome";
            TxtCircleDotBlinkCount.Text       = config.CircleDotBlinkCount.ToString();
            TxtCircleDotBlinkDurationMs.Text  = config.CircleDotBlinkDurationMs.ToString();
            TxtMultiRequestTimeoutMs.Text     = config.MultiRequestTimeoutMs.ToString();
            TxtSystemPrompt.Text              = config.SystemPrompt ?? string.Empty;

            SetNotificationMode(config.NotificationMode);
            SetMultiRequestMode(config.MultiRequestMode);
            HideValidation();
        }

        private AppConfig BuildConfigFromInputs()
        {
            var priority = LstPriority.Items.Cast<ModelEntry>().ToList();

            int historyLimit;
            if (!int.TryParse(TxtHistoryLimit.Text, out historyLimit))
                historyLimit = 10;

            return new AppConfig
            {
                ModelPriority             = priority,
                // Keep legacy fields consistent so old code paths still work
                ApiBaseUrl                = priority.Count > 0 ? priority[0].BaseUrl : string.Empty,
                ApiKey                    = priority.Count > 0 ? priority[0].ApiKey  : string.Empty,
                Models                    = priority.Select(e => e.Model).Distinct().ToList(),
                Endpoints                 = new List<EndpointConfig>(),
                SystemPrompt              = TxtSystemPrompt.Text,
                HistoryLimit              = historyLimit,
                NotificationMode          = GetNotificationMode(),
                MultiRequestMode          = GetMultiRequestMode(),
                MultiRequestTimeoutMs     = int.TryParse(TxtMultiRequestTimeoutMs.Text, out int timeout) ? timeout : 60000,
                FlashWindowTarget         = TxtFlashWindowTarget.Text.Trim(),
                FlashWindowDurationMs     = _configManager.Current.FlashWindowDurationMs,
                CircleDotPulseMs          = _configManager.Current.CircleDotPulseMs,
                CircleDotBlinkCount       = int.TryParse(TxtCircleDotBlinkCount.Text, out int blinkCount) ? blinkCount : 3,
                CircleDotBlinkDurationMs  = int.TryParse(TxtCircleDotBlinkDurationMs.Text, out int blinkDuration) ? blinkDuration : 600,
                RoundRobinIndex           = _configManager.Current.RoundRobinIndex
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

            _controller?.RefreshConfiguration();
            _logger.Info("Settings saved and configuration refreshed.");
            HideValidation();
            return true;
        }

        // ── Footer buttons ────────────────────────────────────────────────────

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (TrySave())
                Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ── Priority list — CRUD & ordering ──────────────────────────────────

        private void OnAddEntry(object sender, RoutedEventArgs e)
        {
            _editingEntryIndex = -1;
            LblEntryEditTitle.Text = "Add Entry";
            ClearEntryForm();
            ShowEntryEditPanel();
        }

        private void OnEditEntry(object sender, RoutedEventArgs e)
        {
            int index = LstPriority.SelectedIndex;
            if (index < 0)
                return;

            _editingEntryIndex = index;
            LblEntryEditTitle.Text = "Edit Entry";
            PopulateEntryForm((ModelEntry)LstPriority.Items[index]);
            ShowEntryEditPanel();
        }

        private void OnRemoveEntry(object sender, RoutedEventArgs e)
        {
            int index = LstPriority.SelectedIndex;
            if (index < 0)
                return;

            LstPriority.Items.RemoveAt(index);

            if (LstPriority.Items.Count > 0)
                LstPriority.SelectedIndex = Math.Min(index, LstPriority.Items.Count - 1);
        }

        private void OnMoveEntryUp(object sender, RoutedEventArgs e)
        {
            int index = LstPriority.SelectedIndex;
            if (index <= 0)
                return;

            var item = LstPriority.Items[index];
            LstPriority.Items.RemoveAt(index);
            LstPriority.Items.Insert(index - 1, item);
            LstPriority.SelectedIndex = index - 1;
        }

        private void OnMoveEntryDown(object sender, RoutedEventArgs e)
        {
            int index = LstPriority.SelectedIndex;
            if (index < 0 || index >= LstPriority.Items.Count - 1)
                return;

            var item = LstPriority.Items[index];
            LstPriority.Items.RemoveAt(index);
            LstPriority.Items.Insert(index + 1, item);
            LstPriority.SelectedIndex = index + 1;
        }

        // ── Inline entry edit form ────────────────────────────────────────────

        private void OnConfirmEntry(object sender, RoutedEventArgs e)
        {
            string name   = TxtEntryName.Text.Trim();
            string url    = TxtEntryBaseUrl.Text.Trim();
            string apiKey = PwdEntryApiKey.Password;
            string model  = TxtEntryModel.Text.Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                ShowValidation("Base URL is required.");
                return;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                ShowValidation("Model is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
                name = "Custom";

            var entry = new ModelEntry
            {
                Name    = name,
                BaseUrl = url.TrimEnd('/'),
                ApiKey  = apiKey,
                Model   = model
            };

            if (_editingEntryIndex == -1)
            {
                // Insert after current selection, or at end
                int insertAt = LstPriority.SelectedIndex >= 0
                    ? LstPriority.SelectedIndex + 1
                    : LstPriority.Items.Count;
                LstPriority.Items.Insert(insertAt, entry);
                LstPriority.SelectedIndex = insertAt;
            }
            else
            {
                LstPriority.Items[_editingEntryIndex] = entry;
                LstPriority.SelectedIndex = _editingEntryIndex;
            }

            HideEntryEditPanel();
            HideValidation();
        }

        private void OnDiscardEntry(object sender, RoutedEventArgs e)
        {
            HideEntryEditPanel();
            HideValidation();
        }

        private void OnEntryPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingEntryPreset)
                return;

            var preset = CboEntryPreset.SelectedItem as EndpointPreset;
            if (preset == null || string.IsNullOrWhiteSpace(preset.BaseUrl))
                return;

            _isApplyingEntryPreset = true;
            TxtEntryBaseUrl.Text = preset.BaseUrl;
            if (string.IsNullOrWhiteSpace(TxtEntryName.Text))
                TxtEntryName.Text = preset.Name;
            if (string.IsNullOrWhiteSpace(TxtEntryModel.Text) && !string.IsNullOrWhiteSpace(preset.DefaultModel))
                TxtEntryModel.Text = preset.DefaultModel;
            _isApplyingEntryPreset = false;
        }

        private void OnEntryBaseUrlChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingEntryPreset)
                return;

            var customPreset = CboEntryPreset.Items
                .OfType<EndpointPreset>()
                .FirstOrDefault(p => p.Name == "Custom");

            if (customPreset != null && !ReferenceEquals(CboEntryPreset.SelectedItem, customPreset))
            {
                _isSyncingEntryPreset = true;
                CboEntryPreset.SelectedItem = customPreset;
                _isSyncingEntryPreset = false;
            }
        }

        // ── Entry form helpers ────────────────────────────────────────────────

        private void ClearEntryForm()
        {
            TxtEntryName.Text    = string.Empty;
            TxtEntryBaseUrl.Text = string.Empty;
            PwdEntryApiKey.Clear();
            TxtEntryModel.Text   = string.Empty;

            _isSyncingEntryPreset = true;
            CboEntryPreset.SelectedIndex = -1;
            _isSyncingEntryPreset = false;
        }

        private void PopulateEntryForm(ModelEntry entry)
        {
            TxtEntryName.Text    = entry.Name    ?? string.Empty;
            TxtEntryBaseUrl.Text = entry.BaseUrl ?? string.Empty;
            PwdEntryApiKey.Password = entry.ApiKey ?? string.Empty;
            TxtEntryModel.Text   = entry.Model   ?? string.Empty;

            // Try to match a preset from the URL
            var matchedPreset = CboEntryPreset.Items
                .OfType<EndpointPreset>()
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.BaseUrl)
                    && p.BaseUrl.TrimEnd('/').Equals(
                        (entry.BaseUrl ?? string.Empty).TrimEnd('/'),
                        StringComparison.OrdinalIgnoreCase));

            _isSyncingEntryPreset = true;
            CboEntryPreset.SelectedItem = matchedPreset
                ?? CboEntryPreset.Items.OfType<EndpointPreset>().FirstOrDefault(p => p.Name == "Custom");
            _isSyncingEntryPreset = false;
        }

        private void ShowEntryEditPanel()
        {
            PnlEntryEdit.Visibility = Visibility.Visible;
            TxtEntryName.Focus();
        }

        private void HideEntryEditPanel()
        {
            PnlEntryEdit.Visibility = Visibility.Collapsed;
            _editingEntryIndex = -2;
        }

        // ── Notification / multi-request mode ────────────────────────────────

        private NotificationMode GetNotificationMode()
        {
            if (RbCircleDot.IsChecked == true) return NotificationMode.CircleDot;
            if (RbNothing.IsChecked   == true) return NotificationMode.Nothing;
            return NotificationMode.FlashWindow;
        }

        private MultiRequestMode GetMultiRequestMode()
        {
            if (RbSequential.IsChecked == true) return MultiRequestMode.Sequential;
            if (RbRoundRobin.IsChecked == true) return MultiRequestMode.RoundRobin;
            return MultiRequestMode.Fastest;
        }

        private void SetNotificationMode(NotificationMode mode)
        {
            RbFlashWindow.IsChecked = mode == NotificationMode.FlashWindow;
            RbCircleDot.IsChecked   = mode == NotificationMode.CircleDot;
            RbNothing.IsChecked     = mode == NotificationMode.Nothing;
            UpdateNotificationSettingsVisibility(mode);
        }

        private void SetMultiRequestMode(MultiRequestMode mode)
        {
            RbSequential.IsChecked = mode == MultiRequestMode.Sequential;
            RbRoundRobin.IsChecked = mode == MultiRequestMode.RoundRobin;
            RbFastest.IsChecked    = mode == MultiRequestMode.Fastest;
        }

        private void OnMultiRequestModeChanged(object sender, RoutedEventArgs e) { }

        private void OnNotificationModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateNotificationSettingsVisibility(GetNotificationMode());
        }

        private void UpdateNotificationSettingsVisibility(NotificationMode mode)
        {
            if (PnlFlashWindowSettings != null)
                PnlFlashWindowSettings.Visibility =
                    mode == NotificationMode.FlashWindow ? Visibility.Visible : Visibility.Collapsed;

            if (PnlCircleDotSettings != null)
                PnlCircleDotSettings.Visibility =
                    mode == NotificationMode.CircleDot ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Validation bar ────────────────────────────────────────────────────

        private void ShowValidation(string message)
        {
            LblValidationError.Text       = message;
            ValidationErrorBar.Visibility = Visibility.Visible;
        }

        private void HideValidation()
        {
            ValidationErrorBar.Visibility = Visibility.Collapsed;
            LblValidationError.Text       = string.Empty;
        }
    }
}
