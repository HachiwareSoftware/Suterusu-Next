using System;
using System.Windows.Forms;
using Suterusu.Bootstrap;
using Suterusu.Configuration;
using Suterusu.Hooks;
using Suterusu.Models;
using Suterusu.Notifications;
using Suterusu.Services;

namespace Suterusu.Application
{
    /// <summary>
    /// Hidden WinForms application context that owns all runtime services,
    /// the keyboard hook, and the application lifetime.
    /// </summary>
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
        private          AppConfig              _config;

        public HeadlessApplicationContext(StartupOptions options)
        {
            _logger.Debug("Initializing application...");

            // --- Build config ---
            _configManager = new ConfigManager(new NLogLogger("Suterusu.Config"));
            _config = _configManager.LoadOrCreateDefault();
            _logger.Debug($"Config loaded: {_config.ModelPriority?.Count ?? 0} model priority entries");

            // --- Build services ---
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

            // --- Keyboard hook ---
            _keyboardHook = new KeyboardHook(new NLogLogger("Suterusu.Hook"));
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

            PrintStartupBanner(options.DebugEnabled);
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
            _logger.Info("  F6 - Clear chat history");
            _logger.Info("  F7 - Read clipboard and send to API");
            _logger.Info("  F8 - Replace clipboard with API response");
            _logger.Info("  F12 - Quit application");
            _logger.Info("");
        }

        private void HandleHotkey(object sender, GlobalHotkey hotkey)
        {
            _logger.Info($"Hotkey: {hotkey}");

            switch (hotkey)
            {
                case GlobalHotkey.F6:
                    _controller.ClearHistory();
                    break;

                case GlobalHotkey.F7:
                    _controller.EnqueueClipboardSend();
                    break;

                case GlobalHotkey.F8:
                    _controller.CopyLastResponseToClipboard();
                    break;

                case GlobalHotkey.F12:
                    _logger.Info("F12 pressed – exiting.");
                    ExitThread();
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keyboardHook?.Dispose();
                _controller?.Dispose();
                _aiClient?.Dispose();
                (_notificationService as IDisposable)?.Dispose();
                _logger.Info("Suterusu shutdown.");
            }

            base.Dispose(disposing);
        }
    }
}
