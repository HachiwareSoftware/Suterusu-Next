using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private readonly Dictionary<GlobalHotkey, string> _hotkeyBindings = new Dictionary<GlobalHotkey, string>();

        // -2 = not in edit mode, -1 = adding new entry, >= 0 = editing existing entry
        private int  _editingEntryIndex    = -2;
        private bool _isApplyingEntryPreset;
        private bool _isSyncingEntryPreset;
        private bool _cliProxyBusy;
        private GlobalHotkey? _capturingHotkey;

        public SettingsWindow(ConfigManager configManager, ClipboardAiController controller = null, Action<AppConfig> configSaved = null)
        {
            _configManager = configManager;
            _controller    = controller;
            _configSaved   = configSaved;
            _cliProxyManager = new CliProxyProcessManager(new NLogLogger("Suterusu.CliProxy"));

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

            var cliProxy = config.CliProxy ?? CliProxySettings.CreateDefault();
            ChkCliProxyEnabled.IsChecked = cliProxy.Enabled;
            ChkCliProxyAutoStart.IsChecked = cliProxy.AutoStart;
            TxtCliProxyModel.Text = string.IsNullOrWhiteSpace(cliProxy.Model) ? "gpt-5.3-codex" : cliProxy.Model;
            TxtCliProxyEndpoint.Text = BuildCliProxyBaseUrl(cliProxy);
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
            if (ocrProvider == OcrProvider.LlamaCpp)
                RbOcrLlamaCpp.IsChecked = true;
            else if (ocrProvider == OcrProvider.Zai)
                RbOcrZai.IsChecked = true;
            else if (ocrProvider == OcrProvider.Custom)
                RbOcrCustom.IsChecked = true;
            else
                RbOcrHuggingFace.IsChecked = true;

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
            var priority = LstPriority.Items.Cast<ModelEntry>().ToList();

            int historyLimit;
            if (!int.TryParse(TxtHistoryLimit.Text, out historyLimit))
                historyLimit = 10;

            var currentCliProxy = _configManager.Current?.CliProxy ?? CliProxySettings.CreateDefault();
            var cliProxyModel = string.IsNullOrWhiteSpace(TxtCliProxyModel.Text)
                ? currentCliProxy.Model
                : TxtCliProxyModel.Text.Trim();

            return new AppConfig
            {
                ModelPriority            = priority,
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
                    HfUrl          = TxtOcrHfUrl.Text,
                    HfToken        = PwdOcrHfToken.Password,
                    HfModel        = TxtOcrHfModel.Text,
                    UseClipboardPrompt = ChkOcrUseClipboardPrompt.IsChecked ?? false
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
                    Model = cliProxyModel,
                    OAuthCallbackPort = currentCliProxy.OAuthCallbackPort
                }
            };
        }

        private bool TrySave()
        {
            AppConfig config = BuildConfigFromInputs();

            if (config.CliProxy?.Enabled == true)
                UpsertCliProxyModelEntry(config);

            var errors = config.Validate();

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
            _cliProxyManager.Dispose();
            base.OnClosed(e);
        }

        // ── CLI proxy ─────────────────────────────────────────────────────────

        private async void OnCliProxyConnectAndUse(object sender, RoutedEventArgs e)
        {
            if (_cliProxyBusy)
                return;

            HideValidation();
            SetCliProxyBusy(true);
            UpdateCliProxyStatus("Starting browser login...");

            try
            {
                var config = BuildConfigFromInputs().Normalize();
                config.CliProxy.Enabled = true;
                config.CliProxy.AutoStart = ChkCliProxyAutoStart.IsChecked ?? true;

                var loginResult = await _cliProxyManager
                    .LoginWithBrowserOAuthAsync(config, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!loginResult.Success)
                {
                    ShowValidation("CLI proxy login failed: " + loginResult.Error);
                    UpdateCliProxyStatus("Login failed.");
                    return;
                }

                UpdateCliProxyStatus("Login complete. Starting local proxy...");

                var startResult = await _cliProxyManager
                    .StartAsync(config, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!startResult.Success)
                {
                    ShowValidation("Failed to start CLI proxy: " + startResult.Error);
                    UpdateCliProxyStatus("Start failed.");
                    return;
                }

                var healthResult = await _cliProxyManager
                    .GetModelsAsync(config, CancellationToken.None)
                    .ConfigureAwait(true);
                if (healthResult.Success && healthResult.Models.Count > 0)
                {
                    if (!healthResult.Models.Contains(config.CliProxy.Model, StringComparer.OrdinalIgnoreCase))
                        config.CliProxy.Model = healthResult.Models[0];

                    TxtCliProxyModel.Text = config.CliProxy.Model;
                }

                UpdateCliProxyStatus("Testing model...");

                var testResult = await _cliProxyManager
                    .TestModelAsync(config, config.CliProxy.Model, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!testResult.Success)
                {
                    ShowValidation("CLI proxy model test failed: " + testResult.Error);
                    UpdateCliProxyStatus("Model test failed.");
                    return;
                }

                UpsertCliProxyModelEntry(config);

                var saveResult = _configManager.Save(config);
                if (!saveResult.Success)
                {
                    ShowValidation(saveResult.Error);
                    UpdateCliProxyStatus("Could not save configuration.");
                    return;
                }

                _controller?.RefreshConfiguration();
                _configSaved?.Invoke(_configManager.Current);
                LoadConfig(_configManager.Current);
                HideValidation();
                UpdateCliProxyStatus($"Connected and saved. Using model '{config.CliProxy.Model}'.");
            }
            catch (Exception ex)
            {
                _logger.Error("CLI proxy connect flow failed.", ex);
                ShowValidation("CLI proxy connect failed: " + ex.Message);
                UpdateCliProxyStatus("Connect flow failed.");
            }
            finally
            {
                SetCliProxyBusy(false);
            }
        }

        private async void OnCliProxyStart(object sender, RoutedEventArgs e)
        {
            await RunCliProxyOperationAsync(async config =>
            {
                config.CliProxy.Enabled = true;
                var start = await _cliProxyManager.StartAsync(config, CancellationToken.None).ConfigureAwait(true);
                if (!start.Success)
                {
                    ShowValidation("Failed to start CLI proxy: " + start.Error);
                    UpdateCliProxyStatus("Start failed.");
                    return;
                }

                HideValidation();
                UpdateCliProxyStatus("CLI proxy is running.");
            });
        }

        private void OnCliProxyStop(object sender, RoutedEventArgs e)
        {
            if (_cliProxyBusy)
                return;

            var stopResult = _cliProxyManager.Stop();
            if (!stopResult.Success)
            {
                ShowValidation("Failed to stop CLI proxy: " + stopResult.Error);
                UpdateCliProxyStatus("Stop failed.");
                return;
            }

            HideValidation();
            UpdateCliProxyStatus("CLI proxy stopped.");
        }

        private async void OnCliProxyTest(object sender, RoutedEventArgs e)
        {
            await RunCliProxyOperationAsync(async config =>
            {
                config.CliProxy.Enabled = true;

                var start = await _cliProxyManager.StartAsync(config, CancellationToken.None).ConfigureAwait(true);
                if (!start.Success)
                {
                    ShowValidation("Failed to start CLI proxy: " + start.Error);
                    UpdateCliProxyStatus("Start failed.");
                    return;
                }

                var test = await _cliProxyManager.TestModelAsync(config, config.CliProxy.Model, CancellationToken.None).ConfigureAwait(true);
                if (!test.Success)
                {
                    ShowValidation("Model test failed: " + test.Error);
                    UpdateCliProxyStatus("Model test failed.");
                    return;
                }

                HideValidation();
                UpdateCliProxyStatus($"Model '{config.CliProxy.Model}' test succeeded.");
            });
        }

        private async void OnCliProxyRefreshModels(object sender, RoutedEventArgs e)
        {
            await RunCliProxyOperationAsync(async config =>
            {
                config.CliProxy.Enabled = true;

                var start = await _cliProxyManager.StartAsync(config, CancellationToken.None).ConfigureAwait(true);
                if (!start.Success)
                {
                    ShowValidation("Failed to start CLI proxy: " + start.Error);
                    UpdateCliProxyStatus("Start failed.");
                    return;
                }

                var health = await _cliProxyManager.GetModelsAsync(config, CancellationToken.None).ConfigureAwait(true);
                if (!health.Success)
                {
                    ShowValidation("Could not fetch models: " + health.Error);
                    UpdateCliProxyStatus("Model detection failed.");
                    return;
                }

                if (health.Models.Count == 0)
                {
                    UpdateCliProxyStatus("Connected but no models were reported.");
                    return;
                }

                if (!health.Models.Contains(config.CliProxy.Model, StringComparer.OrdinalIgnoreCase))
                    config.CliProxy.Model = health.Models[0];

                TxtCliProxyModel.Text = config.CliProxy.Model;
                HideValidation();
                UpdateCliProxyStatus($"Detected {health.Models.Count} model(s). Using '{config.CliProxy.Model}'.");
            });
        }

        private async Task RunCliProxyOperationAsync(Func<AppConfig, Task> operation)
        {
            if (_cliProxyBusy)
                return;

            SetCliProxyBusy(true);
            HideValidation();

            try
            {
                var config = BuildConfigFromInputs().Normalize();
                config.CliProxy.Enabled = ChkCliProxyEnabled.IsChecked ?? config.CliProxy.Enabled;
                await operation(config).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Error("CLI proxy operation failed.", ex);
                ShowValidation("CLI proxy operation failed: " + ex.Message);
            }
            finally
            {
                SetCliProxyBusy(false);
            }
        }

        private void SetCliProxyBusy(bool busy)
        {
            _cliProxyBusy = busy;

            BtnCliProxyConnect.IsEnabled = !busy;
            BtnCliProxyStart.IsEnabled = !busy;
            BtnCliProxyStop.IsEnabled = !busy;
            BtnCliProxyTest.IsEnabled = !busy;
            BtnCliProxyModels.IsEnabled = !busy;

            BtnCliProxyConnect.Content = busy
                ? "Working..."
                : "Connect ChatGPT & Use";
        }

        private void UpdateCliProxyStatus(string message)
        {
            TxtCliProxyStatus.Text = string.IsNullOrWhiteSpace(message)
                ? ""
                : message;
        }

        private static string BuildCliProxyBaseUrl(CliProxySettings settings)
        {
            var effective = settings ?? CliProxySettings.CreateDefault();
            string host = string.IsNullOrWhiteSpace(effective.Host) ? "127.0.0.1" : effective.Host.Trim();
            int port = effective.Port <= 0 ? 8317 : effective.Port;
            return $"http://{host}:{port}/v1";
        }

        private static void UpsertCliProxyModelEntry(AppConfig config)
        {
            if (config.ModelPriority == null)
                config.ModelPriority = new List<ModelEntry>();

            string baseUrl = BuildCliProxyBaseUrl(config.CliProxy).TrimEnd('/');
            string model = string.IsNullOrWhiteSpace(config.CliProxy.Model)
                ? "gpt-5.3-codex"
                : config.CliProxy.Model.Trim();

            var existing = config.ModelPriority.FirstOrDefault(entry =>
                !string.IsNullOrWhiteSpace(entry.BaseUrl)
                && entry.BaseUrl.TrimEnd('/').Equals(baseUrl, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = config.ModelPriority.FirstOrDefault(entry =>
                    !string.IsNullOrWhiteSpace(entry.Name)
                    && entry.Name.Equals("ChatGPT (CLIProxyAPI)", StringComparison.OrdinalIgnoreCase));
            }

            if (existing == null)
            {
                existing = new ModelEntry();
                config.ModelPriority.Insert(0, existing);
            }
            else
            {
                config.ModelPriority.Remove(existing);
                config.ModelPriority.Insert(0, existing);
            }

            existing.Name = "ChatGPT (CLIProxyAPI)";
            existing.BaseUrl = baseUrl;
            existing.ApiKey = config.CliProxy.ApiKey;
            existing.Model = model;
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
            if (RbOcrLlamaCpp.IsChecked == true) return OcrProvider.LlamaCpp;
            if (RbOcrZai.IsChecked == true) return OcrProvider.Zai;
            if (RbOcrCustom.IsChecked == true) return OcrProvider.Custom;
            return OcrProvider.HuggingFace;
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

        private void OnOcrProviderChanged(object sender, RoutedEventArgs e)
        {
            UpdateOcrProviderVisibility(GetOcrProvider());
        }

        private void UpdateOcrProviderVisibility(OcrProvider provider)
        {
            if (PnlLlamaCppSettings == null || PnlZaiSettings == null || PnlCustomSettings == null || PnlHfSettings == null)
                return;

            PnlLlamaCppSettings.Visibility = provider == OcrProvider.LlamaCpp ? Visibility.Visible : Visibility.Collapsed;
            PnlZaiSettings.Visibility = provider == OcrProvider.Zai ? Visibility.Visible : Visibility.Collapsed;
            PnlCustomSettings.Visibility = provider == OcrProvider.Custom ? Visibility.Visible : Visibility.Collapsed;
            PnlHfSettings.Visibility = provider == OcrProvider.HuggingFace ? Visibility.Visible : Visibility.Collapsed;
        }

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
    }
}
