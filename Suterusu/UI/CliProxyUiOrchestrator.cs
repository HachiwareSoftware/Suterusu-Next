using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Suterusu.Configuration;
using Suterusu.Services;

namespace Suterusu.UI
{
    internal class CliProxyUiOrchestrator
    {
        private readonly CliProxyProcessManager _manager;
        private readonly ConfigManager _configManager;
        private readonly ClipboardAiController _controller;
        private readonly Action<AppConfig> _configSaved;
        private readonly Func<AppConfig> _buildConfig;
        private readonly Action<string> _showValidation;
        private readonly Action _hideValidation;
        private readonly Action<string> _updateStatus;
        private readonly ILogger _logger;
        private readonly CancellationToken _windowCt;
        private readonly TextBlock _versionText;
        private readonly TextBlock _installPathText;
        private readonly TextBox _endpointText;
        private readonly CheckBox _enabledCheck;
        private readonly CheckBox _autoStartCheck;

        // All buttons the orchestrator manages
        private readonly Button _connectBtn;
        private readonly Button _startBtn;
        private readonly Button _stopBtn;
        private readonly Button _testBtn;
        private readonly Button _modelsBtn;
        private readonly Button _checkUpdatesBtn;
        private readonly Button _updateBtn;

        private bool _busy;
        private string _lastKnownLatestVersion;

        public CliProxyUiOrchestrator(
            CliProxyProcessManager manager,
            ConfigManager configManager,
            ClipboardAiController controller,
            Action<AppConfig> configSaved,
            Func<AppConfig> buildConfig,
            Action<string> showValidation,
            Action hideValidation,
            Action<string> updateStatus,
            ILogger logger,
            CancellationToken windowCt,
            TextBlock versionText,
            TextBlock installPathText,
            TextBox endpointText,
            CheckBox enabledCheck,
            CheckBox autoStartCheck,
            Button connectBtn,
            Button startBtn,
            Button stopBtn,
            Button testBtn,
            Button modelsBtn,
            Button checkUpdatesBtn,
            Button updateBtn)
        {
            _manager = manager;
            _configManager = configManager;
            _controller = controller;
            _configSaved = configSaved;
            _buildConfig = buildConfig;
            _showValidation = showValidation;
            _hideValidation = hideValidation;
            _updateStatus = updateStatus;
            _logger = logger;
            _windowCt = windowCt;
            _versionText = versionText;
            _installPathText = installPathText;
            _endpointText = endpointText;
            _enabledCheck = enabledCheck;
            _autoStartCheck = autoStartCheck;
            _connectBtn = connectBtn;
            _startBtn = startBtn;
            _stopBtn = stopBtn;
            _testBtn = testBtn;
            _modelsBtn = modelsBtn;
            _checkUpdatesBtn = checkUpdatesBtn;
            _updateBtn = updateBtn;
        }

        public async Task OnConnectAndUse()
        {
            if (_busy)
                return;

            _hideValidation();
            SetBusy(true);
            _updateStatus("Starting browser login...");

            try
            {
                var config = _buildConfig().Normalize();
                config.CliProxy.Enabled = true;
                config.CliProxy.AutoStart = _autoStartCheck.IsChecked ?? true;

                var login = await _manager.LoginWithBrowserOAuthAsync(config, CancellationToken.None);
                if (!login.Success)
                {
                    _showValidation("CLI proxy login failed: " + login.Error);
                    _updateStatus("Login failed.");
                    return;
                }

                _updateStatus("Login complete. Starting local proxy...");

                var start = await _manager.StartAsync(config, CancellationToken.None);
                if (!start.Success)
                {
                    _showValidation("Failed to start CLI proxy: " + start.Error);
                    _updateStatus("Start failed.");
                    return;
                }

                var health = await _manager.GetModelsAsync(config, CancellationToken.None);
                if (health.Success && health.Models.Count > 0)
                {
                    if (!health.Models.Contains(config.CliProxy.Model, StringComparer.OrdinalIgnoreCase))
                        config.CliProxy.Model = health.Models[0];
                }

                _updateStatus("Testing model...");

                var test = await _manager.TestModelAsync(config, config.CliProxy.Model, CancellationToken.None);
                if (!test.Success)
                {
                    _showValidation("CLI proxy model test failed: " + test.Error);
                    _updateStatus("Model test failed.");
                    return;
                }

                var save = _configManager.Save(config);
                if (!save.Success)
                {
                    _showValidation(save.Error);
                    _updateStatus("Could not save configuration.");
                    return;
                }

                _controller?.RefreshConfiguration();
                _configSaved?.Invoke(_configManager.Current);
                _hideValidation();
                _updateStatus($"Connected and saved. Using model '{config.CliProxy.Model}'.");
            }
            catch (Exception ex)
            {
                _logger.Error("CLI proxy connect flow failed.", ex);
                _showValidation("CLI proxy connect failed: " + ex.Message);
                _updateStatus("Connect flow failed.");
            }
            finally
            {
                SetBusy(false);
            }
        }

        public async Task OnStart()
        {
            await RunSafe(async config =>
            {
                config.CliProxy.Enabled = true;
                var result = await _manager.StartAsync(config, CancellationToken.None);
                if (!result.Success)
                    return ("Failed to start CLI proxy: " + result.Error, "Start failed.");

                return (null, "CLI proxy is running.");
            });
        }

        public void OnStop()
        {
            if (_busy)
                return;

            var result = _manager.Stop();
            if (!result.Success)
            {
                _showValidation("Failed to stop CLI proxy: " + result.Error);
                _updateStatus("Stop failed.");
                return;
            }

            _hideValidation();
            _updateStatus("CLI proxy stopped.");
        }

        public async Task OnTest()
        {
            await RunSafe(async config =>
            {
                config.CliProxy.Enabled = true;

                var start = await _manager.StartAsync(config, CancellationToken.None);
                if (!start.Success)
                    return ("Failed to start CLI proxy: " + start.Error, "Start failed.");

                var test = await _manager.TestModelAsync(config, config.CliProxy.Model, CancellationToken.None);
                if (!test.Success)
                    return ("Model test failed: " + test.Error, "Model test failed.");

                return (null, $"Model '{config.CliProxy.Model}' test succeeded.");
            });
        }

        public async Task OnRefreshModels()
        {
            await RunSafe(async config =>
            {
                config.CliProxy.Enabled = true;

                var start = await _manager.StartAsync(config, CancellationToken.None);
                if (!start.Success)
                    return ("Failed to start CLI proxy: " + start.Error, "Start failed.");

                var health = await _manager.GetModelsAsync(config, CancellationToken.None);
                if (!health.Success)
                    return ("Could not fetch models: " + health.Error, "Model detection failed.");

                if (health.Models.Count == 0)
                    return (null, "Connected but no models were reported.");

                if (!health.Models.Contains(config.CliProxy.Model, StringComparer.OrdinalIgnoreCase))
                    config.CliProxy.Model = health.Models[0];

                return (null, $"Detected {health.Models.Count} model(s). Using '{config.CliProxy.Model}'.");
            });
        }

        public async Task LoadVersionStatusAsync()
        {
            _versionText.Text = "CLIProxyAPI: checking...";
            UpdateInstallPath();

            string installed = _manager.GetInstalledVersion(_configManager.Current);
            string latest = null;

            try
            {
                using (var checkCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_windowCt, checkCts.Token))
                {
                    latest = await _manager.GetLatestVersionAsync(linked.Token);
                    _lastKnownLatestVersion = latest;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Warn($"Could not check CLIProxyAPI latest version: {ex.Message}");
            }

            UpdateVersionStatus(installed, latest);
            UpdateInstallPath();
        }

        public async Task OnCheckForUpdates()
        {
            if (_busy)
                return;

            _checkUpdatesBtn.IsEnabled = false;
            try
            {
                await LoadVersionStatusAsync();
            }
            finally
            {
                _checkUpdatesBtn.IsEnabled = !_busy;
            }
        }

        public async Task OnUpdate()
        {
            if (_busy)
                return;

            SetBusy(true);
            _hideValidation();

            try
            {
                _manager.Stop();

                var config = _buildConfig().Normalize();
                var progress = new Progress<string>(msg => _updateStatus(msg));

                var result = await _manager.UpdateCliProxyAsync(config, progress, _windowCt);
                if (!result.Success)
                {
                    _showValidation("Update failed: " + result.Error);
                    _versionText.Text = "CLIProxyAPI: update failed.";
                    return;
                }

                _hideValidation();
                _updateStatus("CLIProxyAPI installed/updated. You can now connect.");
                string installed = _manager.GetInstalledVersion(config);
                UpdateVersionStatus(installed, _lastKnownLatestVersion);
            }
            catch (OperationCanceledException)
            {
                _versionText.Text = "CLIProxyAPI: update canceled.";
            }
            catch (Exception ex)
            {
                _logger.Error("CLIProxyAPI update failed.", ex);
                _showValidation("Update failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        public void UpdateEndpoint(CliProxySettings settings)
        {
            var effective = settings ?? CliProxySettings.CreateDefault();
            _endpointText.Text = effective.GetApiBaseUrl();
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private async Task RunSafe(Func<AppConfig, Task<(string error, string status)>> operation)
        {
            if (_busy)
                return;

            SetBusy(true);
            _hideValidation();

            try
            {
                var config = _buildConfig().Normalize();
                config.CliProxy.Enabled = _enabledCheck.IsChecked ?? config.CliProxy.Enabled;
                var (error, status) = await operation(config);

                if (error != null)
                {
                    _showValidation(error);
                    _updateStatus(status);
                }
                else
                {
                    _hideValidation();
                    _updateStatus(status);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("CLI proxy operation failed.", ex);
                _showValidation("CLI proxy operation failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;

            _connectBtn.IsEnabled = !busy;
            _startBtn.IsEnabled = !busy;
            _stopBtn.IsEnabled = !busy;
            _testBtn.IsEnabled = !busy;
            _modelsBtn.IsEnabled = !busy;
            _checkUpdatesBtn.IsEnabled = !busy;
            _updateBtn.IsEnabled = !busy;

            _connectBtn.Content = busy ? "Working..." : "Connect ChatGPT & Use";
        }

        private void UpdateInstallPath()
        {
            var settings = _configManager.Current?.CliProxy;
            string dir = string.IsNullOrWhiteSpace(settings?.RuntimeDirectory)
                ? CliProxySettings.GetDefaultRuntimeDirectory()
                : settings.RuntimeDirectory;
            _installPathText.Text = "Install path: " + dir;
        }

        private void UpdateVersionStatus(string installedVersion, string latestVersion)
        {
            bool versionUnknown = installedVersion == "(installed)";

            if (installedVersion == null && latestVersion == null)
                _versionText.Text = "CLIProxyAPI: not installed";
            else if (installedVersion == null)
                _versionText.Text = $"CLIProxyAPI: not installed — {latestVersion} available";
            else if (versionUnknown && latestVersion == null)
                _versionText.Text = "CLIProxyAPI: installed (version unknown)";
            else if (versionUnknown)
                _versionText.Text = $"CLIProxyAPI: installed — latest: {latestVersion}";
            else if (latestVersion == null)
                _versionText.Text = $"CLIProxyAPI: {installedVersion} (could not check latest)";
            else if (string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
                _versionText.Text = $"CLIProxyAPI: {installedVersion} — up to date";
            else
                _versionText.Text = $"CLIProxyAPI: {installedVersion} — {latestVersion} available";
        }
    }
}
