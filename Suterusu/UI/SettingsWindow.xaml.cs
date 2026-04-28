using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Suterusu.Configuration;
using Suterusu.Models;
using Suterusu.Services;

namespace Suterusu.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly ConfigManager         _configManager;
        private readonly ClipboardAiController _controller;
        private readonly Action<AppConfig>     _configSaved;
        private readonly ILogger               _logger = new NLogLogger("Suterusu.Settings");
        private readonly CliProxyProcessManager _cliProxyManager;
        private readonly CliProxyUiOrchestrator _cliProxyOrchestrator;
        private readonly ModelPriorityEditor _modelEditor;
        private readonly OcrSettingsHelper _ocrHelper;
        private readonly CancellationTokenSource _windowCts = new CancellationTokenSource();
        private readonly Dictionary<GlobalHotkey, string> _hotkeyBindings = new Dictionary<GlobalHotkey, string>();
        private GlobalHotkey? _capturingHotkey;

        public SettingsWindow(ConfigManager configManager, ClipboardAiController controller = null, Action<AppConfig> configSaved = null)
        {
            _configManager = configManager;
            _controller    = controller;
            _configSaved   = configSaved;
            _cliProxyManager = new CliProxyProcessManager(new NLogLogger("Suterusu.CliProxy"));

            InitializeComponent();

            RbFlashWindow.IsChecked    = true;
            RbSequential.IsChecked     = true;
            InitializeOcrProviderDropdown();

            _modelEditor = new ModelPriorityEditor(
                LstPriority, PnlEntryEdit, LblEntryEditTitle,
                TxtEntryName, TxtEntryBaseUrl, PwdEntryApiKey,
                CboEntryModel, BtnFetchEntryModels, CboEntryPreset,
                ShowValidation, HideValidation, _logger);

            _ocrHelper = new OcrSettingsHelper(
                CboWindowsOcrLanguage, TxtWindowsOcrStatus,
                GetOcrProvider, ShowValidation, UpdateCliProxyStatus, _logger);

            _cliProxyOrchestrator = new CliProxyUiOrchestrator(
                _cliProxyManager, _configManager, _controller, _configSaved,
                BuildConfigFromInputs,
                ShowValidation, HideValidation, UpdateCliProxyStatus,
                _logger, _windowCts.Token,
                TxtCliProxyVersion, TxtCliProxyInstallPath, TxtCliProxyEndpoint,
                ChkCliProxyEnabled, ChkCliProxyAutoStart,
                BtnCliProxyConnect, BtnCliProxyStart, BtnCliProxyStop,
                BtnCliProxyTest, BtnCliProxyModels,
                BtnCheckForUpdates, BtnUpdateCliProxy);

            LoadConfig(_configManager.Current);

            _ = _cliProxyOrchestrator.LoadVersionStatusAsync();
        }

        // ── Load / Save ───────────────────────────────────────────────────────

        public void LoadConfig(AppConfig config)
        {
            if (config == null)
                return;

            _modelEditor.LoadListFrom(config.ModelPriority);

            TxtHistoryLimit.Text              = config.HistoryLimit.ToString();
            TxtFlashWindowTarget.Text         = config.FlashWindowTarget ?? "Chrome";
            TxtCircleDotBlinkCount.Text       = config.CircleDotBlinkCount.ToString();
            TxtCircleDotBlinkDurationMs.Text  = config.CircleDotBlinkDurationMs.ToString();
            TxtMultiRequestTimeoutMs.Text     = config.MultiRequestTimeoutMs.ToString();
            TxtSystemPrompt.Text              = config.SystemPrompt ?? string.Empty;

            var cliProxy = config.CliProxy ?? CliProxySettings.CreateDefault();
            ChkCliProxyEnabled.IsChecked = cliProxy.Enabled;
            ChkCliProxyAutoStart.IsChecked = cliProxy.AutoStart;
            _cliProxyOrchestrator.UpdateEndpoint(cliProxy);
            UpdateCliProxyStatus(cliProxy.Enabled
                ? "Configured. Use Start/Test or Connect ChatGPT & Use."
                : "Disabled. Use Connect ChatGPT & Use to set up.");

            _hotkeyBindings[GlobalHotkey.ClearHistory] = HotkeyBindingHelper.NormalizeBindingName(
                config.ClearHistoryHotkey,
                GlobalHotkey.ClearHistory);
            _hotkeyBindings[GlobalHotkey.SendClipboard] = HotkeyBindingHelper.NormalizeBindingName(
                config.SendClipboardHotkey,
                GlobalHotkey.SendClipboard);
            _hotkeyBindings[GlobalHotkey.CopyLastResponse] = HotkeyBindingHelper.NormalizeBindingName(
                config.CopyLastResponseHotkey,
                GlobalHotkey.CopyLastResponse);
            _hotkeyBindings[GlobalHotkey.QuitApplication] = HotkeyBindingHelper.NormalizeBindingName(
                config.QuitApplicationHotkey,
                GlobalHotkey.QuitApplication);
            _hotkeyBindings[GlobalHotkey.RunOcr] = HotkeyBindingHelper.NormalizeBindingName(
                config.Ocr?.Hotkey,
                GlobalHotkey.RunOcr);

            // OCR settings
            var ocrProvider = config.Ocr?.Provider ?? OcrProvider.LlamaCpp;
            foreach (ComboBoxItem item in CboOcrProvider.Items)
            {
                if (item.Tag is OcrProvider p && p == ocrProvider)
                {
                    CboOcrProvider.SelectedItem = item;
                    break;
                }
            }

            _ocrHelper.PopulateLanguageDropdown(config.Ocr?.WindowsOcrLanguage ?? string.Empty);

            TxtOcrLlamaCppUrl.Text = config.Ocr?.LlamaCppUrl ?? "http://localhost:8080";
            CboLlamaCppModel.Text = config.Ocr?.LlamaCppModel ?? "ggml-org/GLM-OCR-GGUF";
            PwdOcrZaiToken.Password = config.Ocr?.ZaiToken ?? string.Empty;
            TxtOcrZaiModel.Text = config.Ocr?.ZaiModel ?? "glm-ocr";
            TxtOcrCustomUrl.Text = config.Ocr?.CustomUrl ?? string.Empty;
            PwdOcrCustomApiKey.Password = config.Ocr?.CustomApiKey ?? string.Empty;
            TxtOcrCustomModel.Text = config.Ocr?.CustomModel ?? string.Empty;
            TxtOcrHfUrl.Text = config.Ocr?.HfUrl ?? "https://api.huggingface.co/v1";
            PwdOcrHfToken.Password = config.Ocr?.HfToken ?? string.Empty;
            TxtOcrHfModel.Text = config.Ocr?.HfModel ?? "google/ocr";
            TxtOcrPrompt.Text = config.Ocr?.Prompt ?? "Recognize all text from this image.";
            ChkOcrUseClipboardPrompt.IsChecked = config.Ocr?.UseClipboardPrompt ?? false;
            TxtOcrTimeoutMs.Text = config.Ocr?.TimeoutMs.ToString() ?? "60000";
            UpdateOcrProviderVisibility(ocrProvider);

            _capturingHotkey = null;
            UpdateHotkeyButtonLabels();

            SetNotificationMode(config.NotificationMode);
            SetMultiRequestMode(config.MultiRequestMode);
            HideValidation();
        }

        private AppConfig BuildConfigFromInputs()
        {
            var priority = _modelEditor.GetEntries();

            int historyLimit;
            if (!int.TryParse(TxtHistoryLimit.Text, out historyLimit))
                historyLimit = 10;

            var currentCliProxy = _configManager.Current?.CliProxy ?? CliProxySettings.CreateDefault();

            return new AppConfig
            {
                ModelPriority            = priority.ToList(),
                SystemPrompt             = TxtSystemPrompt.Text,
                HistoryLimit             = historyLimit,
                NotificationMode         = GetNotificationMode(),
                MultiRequestMode         = GetMultiRequestMode(),
                MultiRequestTimeoutMs    = int.TryParse(TxtMultiRequestTimeoutMs.Text, out int timeout) ? timeout : 60000,
                FlashWindowTarget        = TxtFlashWindowTarget.Text.Trim(),
                FlashWindowDurationMs    = _configManager.Current.FlashWindowDurationMs,
                CircleDotBlinkCount      = int.TryParse(TxtCircleDotBlinkCount.Text, out int blinkCount) ? blinkCount : 3,
                CircleDotBlinkDurationMs = int.TryParse(TxtCircleDotBlinkDurationMs.Text, out int blinkDuration) ? blinkDuration : 600,
                RoundRobinIndex          = _configManager.Current.RoundRobinIndex,
                ClearHistoryHotkey       = GetStoredHotkey(GlobalHotkey.ClearHistory),
                SendClipboardHotkey      = GetStoredHotkey(GlobalHotkey.SendClipboard),
                CopyLastResponseHotkey   = GetStoredHotkey(GlobalHotkey.CopyLastResponse),
                QuitApplicationHotkey    = GetStoredHotkey(GlobalHotkey.QuitApplication),
                Ocr                     = new OcrSettings
                {
                    Enabled        = true,
                    Hotkey         = GetStoredHotkey(GlobalHotkey.RunOcr),
                    Provider       = GetOcrProvider(),
                    Prompt         = TxtOcrPrompt.Text,
                    TimeoutMs      = int.TryParse(TxtOcrTimeoutMs.Text, out int ocrTimeout) ? ocrTimeout : 60000,
                    LlamaCppUrl    = TxtOcrLlamaCppUrl.Text,
                    LlamaCppModel  = CboLlamaCppModel.Text,
                    ZaiToken       = PwdOcrZaiToken.Password,
                    ZaiModel       = TxtOcrZaiModel.Text,
                    CustomUrl      = TxtOcrCustomUrl.Text,
                    CustomApiKey   = PwdOcrCustomApiKey.Password,
                    CustomModel    = TxtOcrCustomModel.Text,
                    HfUrl              = TxtOcrHfUrl.Text,
                    HfToken            = PwdOcrHfToken.Password,
                    HfModel            = TxtOcrHfModel.Text,
                    UseClipboardPrompt = ChkOcrUseClipboardPrompt.IsChecked ?? false,
                    WindowsOcrLanguage = _ocrHelper.GetSelectedWindowsOcrLanguage()
                },
                CliProxy = new CliProxySettings
                {
                    Enabled = ChkCliProxyEnabled.IsChecked ?? false,
                    AutoStart = ChkCliProxyAutoStart.IsChecked ?? true,
                    ExecutablePath = currentCliProxy.ExecutablePath,
                    RuntimeDirectory = currentCliProxy.RuntimeDirectory,
                    ConfigPath = currentCliProxy.ConfigPath,
                    AuthDirectory = currentCliProxy.AuthDirectory,
                    Host = currentCliProxy.Host,
                    Port = currentCliProxy.Port,
                    ApiKey = currentCliProxy.ApiKey,
                    ManagementKey = currentCliProxy.ManagementKey,
                    Model = currentCliProxy.Model,
                    OAuthCallbackPort = currentCliProxy.OAuthCallbackPort
                }
            };
        }

        private bool TrySave()
        {
            AppConfig config = BuildConfigFromInputs();
            var errors = config.Validate().ToList();

            string windowsOcrValidationError = _ocrHelper.GetValidationError(config);
            if (!string.IsNullOrEmpty(windowsOcrValidationError))
                errors.Add(windowsOcrValidationError);

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
            _configSaved?.Invoke(_configManager.Current);
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

        protected override void OnClosed(EventArgs e)
        {
            _windowCts.Cancel();
            _windowCts.Dispose();
            _cliProxyManager.Dispose();
            base.OnClosed(e);
        }

        // ── CLI proxy ─────────────────────────────────────────────────────────

        private async void OnCliProxyConnectAndUse(object sender, RoutedEventArgs e)
            => await _cliProxyOrchestrator.OnConnectAndUse();

        private async void OnCliProxyStart(object sender, RoutedEventArgs e)
            => await _cliProxyOrchestrator.OnStart();

        private void OnCliProxyStop(object sender, RoutedEventArgs e)
            => _cliProxyOrchestrator.OnStop();

        private async void OnCliProxyTest(object sender, RoutedEventArgs e)
            => await _cliProxyOrchestrator.OnTest();

        private async void OnCliProxyRefreshModels(object sender, RoutedEventArgs e)
            => await _cliProxyOrchestrator.OnRefreshModels();

        private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
            => await _cliProxyOrchestrator.OnCheckForUpdates();

        private async void OnUpdateCliProxy(object sender, RoutedEventArgs e)
            => await _cliProxyOrchestrator.OnUpdate();

        private void UpdateCliProxyStatus(string message)
        {
            TxtCliProxyStatus.Text = string.IsNullOrWhiteSpace(message)
                ? ""
                : message;
        }

        // ── Priority list — CRUD & ordering ──────────────────────────────────

        private void OnAddEntry(object sender, RoutedEventArgs e)
            => _modelEditor.OnAdd();

        private void OnEditEntry(object sender, RoutedEventArgs e)
            => _modelEditor.OnEdit();

        private void OnRemoveEntry(object sender, RoutedEventArgs e)
            => _modelEditor.OnDelete();

        private void OnMoveEntryUp(object sender, RoutedEventArgs e)
            => _modelEditor.OnMoveUp();

        private void OnMoveEntryDown(object sender, RoutedEventArgs e)
            => _modelEditor.OnMoveDown();

        private void OnConfirmEntry(object sender, RoutedEventArgs e)
            => _modelEditor.OnConfirm();

        private void OnDiscardEntry(object sender, RoutedEventArgs e)
            => _modelEditor.OnDiscard();

        // ── Hotkey capture ────────────────────────────────────────────────────

        private void OnHotkeyButtonClick(object sender, RoutedEventArgs e)
        {
            GlobalHotkey hotkey = GetHotkeyFromButton((Button)sender);

            if (_capturingHotkey == hotkey)
            {
                _capturingHotkey = null;
                UpdateHotkeyButtonLabels();
                return;
            }

            _capturingHotkey = hotkey;
            UpdateHotkeyButtonLabels();
            HideValidation();
            Focus();
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturingHotkey == null)
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                _capturingHotkey = null;
                UpdateHotkeyButtonLabels();
                e.Handled = true;
                return;
            }

            if (HotkeyBindingHelper.TryBuildBindingFromKeyEvent(key, Keyboard.Modifiers, out string bindingName))
            {
                _hotkeyBindings[_capturingHotkey.Value] = bindingName;
                _capturingHotkey = null;
                UpdateHotkeyButtonLabels();
                HideValidation();
                e.Handled = true;
            }
        }

        private string GetStoredHotkey(GlobalHotkey hotkey)
        {
            return _hotkeyBindings.TryGetValue(hotkey, out string bindingName)
                ? bindingName
                : HotkeyBindingHelper.GetDefaultBinding(hotkey);
        }

        private void UpdateHotkeyButtonLabels()
        {
            BtnClearHistoryHotkey.Content = GetHotkeyButtonText(GlobalHotkey.ClearHistory);
            BtnSendClipboardHotkey.Content = GetHotkeyButtonText(GlobalHotkey.SendClipboard);
            BtnCopyLastResponseHotkey.Content = GetHotkeyButtonText(GlobalHotkey.CopyLastResponse);
            BtnQuitApplicationHotkey.Content = GetHotkeyButtonText(GlobalHotkey.QuitApplication);
            BtnOcrHotkey.Content = GetHotkeyButtonText(GlobalHotkey.RunOcr);
        }

        private string GetHotkeyButtonText(GlobalHotkey hotkey)
        {
            if (_capturingHotkey != hotkey)
                return GetStoredHotkey(hotkey);

            return "Press keys...";
        }

        private GlobalHotkey GetHotkeyFromButton(Button button)
        {
            if (ReferenceEquals(button, BtnClearHistoryHotkey)) return GlobalHotkey.ClearHistory;
            if (ReferenceEquals(button, BtnSendClipboardHotkey)) return GlobalHotkey.SendClipboard;
            if (ReferenceEquals(button, BtnCopyLastResponseHotkey)) return GlobalHotkey.CopyLastResponse;
            if (ReferenceEquals(button, BtnQuitApplicationHotkey)) return GlobalHotkey.QuitApplication;
            if (ReferenceEquals(button, BtnOcrHotkey)) return GlobalHotkey.RunOcr;
            throw new InvalidOperationException("Unknown hotkey button.");
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

        // ── OCR settings ───────────────────────────────────────────────────────

        private OcrProvider GetOcrProvider()
        {
            if (CboOcrProvider.SelectedItem is ComboBoxItem item && item.Tag is OcrProvider p)
                return p;
            return OcrProvider.LlamaCpp;
        }

        private void InitializeOcrProviderDropdown()
        {
            CboOcrProvider.Items.Add(new ComboBoxItem { Content = "llama.cpp",   Tag = OcrProvider.LlamaCpp    });
            CboOcrProvider.Items.Add(new ComboBoxItem { Content = "Z.ai",        Tag = OcrProvider.Zai         });
            CboOcrProvider.Items.Add(new ComboBoxItem { Content = "Custom",      Tag = OcrProvider.Custom      });
            CboOcrProvider.Items.Add(new ComboBoxItem { Content = "HuggingFace", Tag = OcrProvider.HuggingFace });
            CboOcrProvider.Items.Add(new ComboBoxItem { Content = "Windows OCR", Tag = OcrProvider.WindowsOcr  });
            CboOcrProvider.SelectedIndex = 0;
        }

        private void OnOcrEnabledChanged(object sender, RoutedEventArgs e)
        {
            PnlLlamaCppSettings.IsEnabled = true;
            PnlZaiSettings.IsEnabled = true;
            PnlCustomSettings.IsEnabled = true;
            PnlHfSettings.IsEnabled = true;
            TxtOcrPrompt.IsEnabled = true;
            TxtOcrTimeoutMs.IsEnabled = true;
        }

        private void OnOcrProviderChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOcrProviderVisibility(GetOcrProvider());
        }

        private void UpdateOcrProviderVisibility(OcrProvider provider)
        {
            if (PnlLlamaCppSettings == null || PnlZaiSettings == null || PnlCustomSettings == null || PnlHfSettings == null)
                return;

            bool isWindows = provider == OcrProvider.WindowsOcr;

            PnlLlamaCppSettings.Visibility     = provider == OcrProvider.LlamaCpp    ? Visibility.Visible : Visibility.Collapsed;
            PnlZaiSettings.Visibility          = provider == OcrProvider.Zai         ? Visibility.Visible : Visibility.Collapsed;
            PnlCustomSettings.Visibility       = provider == OcrProvider.Custom       ? Visibility.Visible : Visibility.Collapsed;
            PnlHfSettings.Visibility           = provider == OcrProvider.HuggingFace ? Visibility.Visible : Visibility.Collapsed;
            PnlWindowsOcrSettings.Visibility   = isWindows                           ? Visibility.Visible : Visibility.Collapsed;

            // Prompt is irrelevant for Windows OCR — hide it
            if (PnlOcrPromptRow != null)
                PnlOcrPromptRow.Visibility = isWindows ? Visibility.Collapsed : Visibility.Visible;

            _ocrHelper.UpdateStatus();
        }

        private void OnRefreshWindowsOcrLanguages(object sender, RoutedEventArgs e)
            => _ocrHelper.Refresh();

        private async void OnFetchLlamaCppModels(object sender, RoutedEventArgs e)
        {
            var url = TxtOcrLlamaCppUrl.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                ShowValidation("Please enter a llama.cpp server URL first.");
                return;
            }

            BtnFetchLlamaCppModels.IsEnabled = false;
            BtnFetchLlamaCppModels.Content = "Fetching...";
            HideValidation();

            try
            {
                var client = new LlamaCppModelsClient(_logger);
                var models = await client.GetAvailableModelsAsync(url, 10000, CancellationToken.None);

                CboLlamaCppModel.Items.Clear();
                if (models.Count > 0)
                {
                    foreach (var model in models)
                        CboLlamaCppModel.Items.Add(model);
                    CboLlamaCppModel.SelectedIndex = 0;
                }
                else
                {
                    ShowValidation("No models found. Please enter model name manually.");
                }
            }
            catch (Exception ex)
            {
                ShowValidation($"Failed to fetch models: {ex.Message}");
            }
            finally
            {
                BtnFetchLlamaCppModels.IsEnabled = true;
                BtnFetchLlamaCppModels.Content = "Fetch Models";
            }
        }

        private async void OnFetchEntryModels(object sender, RoutedEventArgs e)
            => await _modelEditor.OnFetchModelsAsync();
    }
}
