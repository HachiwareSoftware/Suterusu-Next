using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Suterusu.Bootstrap;
using Suterusu.Configuration;
using Suterusu.Hooks;
using Suterusu.Models;
using Suterusu.Notifications;
using Suterusu.Services;

namespace Suterusu.Application
{
    public class HeadlessApplicationContext : ApplicationContext
    {
        private readonly ILogger                _logger = new NLogLogger("Suterusu.App");
        private readonly ConfigManager          _configManager;
        private readonly ClipboardService       _clipboardService;
        private readonly AiClient               _aiClient;
        private readonly ChatHistory            _chatHistory;
        private readonly INotificationService   _notificationService;
        private readonly ClipboardAiController  _controller;
        private readonly KeyboardHook           _keyboardHook;
        private readonly MouseHook               _mouseHook;
        private readonly CliProxyProcessManager  _cliProxyManager;
        private readonly CdpService              _cdpService;
        private          AppConfig              _config;

        public HeadlessApplicationContext(StartupOptions options)
        {
            _logger.Debug("Initializing application...");

            _configManager = new ConfigManager(new NLogLogger("Suterusu.Config"));
            _config = _configManager.LoadOrCreateDefault();
            _logger.Debug($"Config loaded: {_config.ModelPriority?.Count ?? 0} model priority entries");

            _cliProxyManager = new CliProxyProcessManager(new NLogLogger("Suterusu.CliProxy"));

            _clipboardService    = new ClipboardService(new NLogLogger("Suterusu.Clipboard"));
            _aiClient            = new AiClient(new NLogLogger("Suterusu.AI"));
            _notificationService = NotificationServiceFactory.Create(_config);
            _chatHistory         = new ChatHistory(_config.SystemPrompt, _config.HistoryLimit);

            _controller = new ClipboardAiController(
                _clipboardService,
                _aiClient,
                _chatHistory,
                _configManager,
                new NLogLogger("Suterusu.Controller"),
                _notificationService);

            var ocrLogger = new NLogLogger("Suterusu.OCR");
            IOcrClient ocrClient = OcrClientFactory.Create(ocrLogger, _config);
            _controller.RefreshOcrClient(ocrClient);

            _keyboardHook = new KeyboardHook(new NLogLogger("Suterusu.Hook"));
            _keyboardHook.UpdateBindings(_config);
            _keyboardHook.HotkeyTriggered += HandleHotkey;

            try
            {
                _keyboardHook.Install();
            }
            catch (Exception ex)
            {
                _logger.Error("Fatal: keyboard hook installation failed.", ex);
                System.Windows.Forms.Application.Exit();
                return;
            }

            _mouseHook = new MouseHook(new NLogLogger("Suterusu.Mouse"));
            _controller.SetMouseHook(_mouseHook);

            try
            {
                _mouseHook.Install();
            }
            catch (Exception ex)
            {
                _logger.Error("Fatal: mouse hook installation failed.", ex);
            }

            TryAutoStartCliProxyAsync();
            if (_config.Cdp?.Enabled == true)
            {
                _cdpService = new CdpService(_config, new CdpFileLogger());
                _cdpService.Start();
            }
            PrintStartupBanner(options.DebugEnabled);
        }

        private void TryAutoStartCliProxyAsync()
        {
            if (_config?.CliProxy?.Enabled != true)
                return;

            if (!_config.CliProxy.AutoStart)
                return;

            Task.Run(async () =>
            {
                try
                {
                    var startResult = await _cliProxyManager.StartAsync(_config, CancellationToken.None).ConfigureAwait(false);
                    if (!startResult.Success)
                        _logger.Warn("CLI proxy auto-start failed: " + startResult.Error);
                    else
                        _logger.Info("CLI proxy auto-start complete.");
                }
                catch (Exception ex)
                {
                    _logger.Error("CLI proxy auto-start crashed.", ex);
                }
            });
        }

        private void PrintStartupBanner(bool debugEnabled)
        {
            string notificationMode;
            switch (_config.NotificationMode)
            {
                case NotificationMode.CircleDot:
                    notificationMode = "Circle Dot";
                    break;
                case NotificationMode.Nothing:
                    notificationMode = "Nothing";
                    break;
                default:
                    notificationMode = $"Flash Window (target={_config.FlashWindowTarget}, duration={_config.FlashWindowDurationMs}ms)";
                    break;
            }

            _logger.Info("");
            _logger.Info("=== Suterusu ===");
            _logger.Info($"Debug mode: {(debugEnabled ? "Enabled" : "Disabled")}");
            _logger.Info("");
            _logger.Info("Configuration loaded successfully:");
            _logger.Info($"  Models: {_config.ModelPriority?.Count ?? 0} entries");
            _logger.Info($"  System Prompt: {_config.SystemPrompt}");
            _logger.Info($"  Notification: {notificationMode}");
            _logger.Info("");
            _logger.Info("Controls:");
            _logger.Info($"  {_config.ClearHistoryHotkey} - Clear chat history");
            _logger.Info($"  {_config.SendClipboardHotkey} - Read clipboard and send to API");
            _logger.Info($"  {_config.Ocr?.Hotkey} - Select screen region for OCR");
            _logger.Info($"  {_config.CopyLastResponseHotkey} - Replace clipboard with API response");
            _logger.Info($"  {_config.QuitApplicationHotkey} - Quit application");
            _logger.Info("");

            var ocrProvider = _config.Ocr?.Provider.ToString().ToLower() ?? "none";
            string ocrModel = "";
            if (_config.Ocr != null)
            {
                switch (_config.Ocr.Provider)
                {
                    case OcrProvider.LlamaCpp:
                        ocrModel = _config.Ocr.LlamaCppModel;
                        break;
                    case OcrProvider.Zai:
                        ocrModel = _config.Ocr.ZaiModel;
                        break;
                    case OcrProvider.Custom:
                        ocrModel = _config.Ocr.CustomModel;
                        break;
                    case OcrProvider.HuggingFace:
                        ocrModel = _config.Ocr.HfModel;
                        break;
                }
            }
            _logger.Info($"OCR: {ocrProvider} (model={ocrModel})");
            if (_config?.CliProxy != null)
            {
                _logger.Info($"CLI proxy: {(_config.CliProxy.Enabled ? "enabled" : "disabled")} ({_config.CliProxy.Host}:{_config.CliProxy.Port}, model={_config.CliProxy.Model})");
            }
            _logger.Info("");
        }

        private void HandleHotkey(object sender, GlobalHotkey hotkey)
        {
            _logger.Info($"Hotkey: {hotkey}");

            switch (hotkey)
            {
                case GlobalHotkey.ClearHistory:
                    _controller.ClearHistory();
                    break;

                case GlobalHotkey.SendClipboard:
                    _controller.EnqueueClipboardSend();
                    break;

                case GlobalHotkey.RunOcr:
                    if (_mouseHook != null && _mouseHook.State != Hooks.SelectionState.Idle)
                    {
                        _controller.CancelOcrSelection();
                    }
                    else
                    {
                        _controller.StartOcrSelection();
                    }
                    break;

                case GlobalHotkey.CopyLastResponse:
                    _controller.CopyLastResponseToClipboard();
                    break;

                case GlobalHotkey.QuitApplication:
                    _logger.Info($"{_config.QuitApplicationHotkey} pressed - exiting.");
                    ExitThread();
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _mouseHook?.Dispose();
                _keyboardHook?.Dispose();
                _controller?.Dispose();
                _aiClient?.Dispose();
                _cliProxyManager?.Dispose();
                _cdpService?.Dispose();
                (_notificationService as IDisposable)?.Dispose();
                _logger.Info("Suterusu shutdown.");
            }

            base.Dispose(disposing);
        }
    }
}
