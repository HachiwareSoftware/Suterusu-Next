using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Suterusu.Configuration;
using Suterusu.Hooks;
using Suterusu.Models;
using Suterusu.Notifications;

namespace Suterusu.Services
{
    public class ClipboardAiController : IDisposable
    {
        private readonly ClipboardService    _clipboardService;
        private readonly AiClient            _aiClient;
        private readonly ChatHistory         _chatHistory;
        private readonly ConfigManager       _configManager;
        private readonly ILogger             _logger;
        private readonly INotificationService _notifications;

        private IOcrClient _ocrClient;
        private MouseHook _mouseHook;
        private ScreenCaptureService _screenCapture;

        private readonly ConcurrentQueue<QueuedClipboardRequest> _pendingRequests
            = new ConcurrentQueue<QueuedClipboardRequest>();

        private int  _processorRunning = 0;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public string LastAiResponse { get; private set; }

        public ClipboardAiController(
            ClipboardService    clipboardService,
            AiClient            aiClient,
            ChatHistory         chatHistory,
            ConfigManager       configManager,
            ILogger             logger,
            INotificationService notifications)
        {
            _clipboardService = clipboardService;
            _aiClient         = aiClient;
            _chatHistory      = chatHistory;
            _configManager    = configManager;
            _logger           = logger;
            _notifications    = notifications;
            _screenCapture    = new ScreenCaptureService();
            _logger.Debug("initialized");
        }

        public void SetMouseHook(MouseHook mouseHook)
        {
            _mouseHook = mouseHook;
            _mouseHook.SelectionComplete += OnSelectionComplete;
            _mouseHook.SelectionCancelled += OnSelectionCancelled;
        }

        // ----- Public hotkey actions -----

        public void ClearHistory()
        {
            _logger.Debug("resetting chat history");
            _chatHistory.Reset(_configManager.Current.SystemPrompt);
            _logger.Info("Chat history cleared.");
        }

        public void EnqueueClipboardSend()
        {
            _pendingRequests.Enqueue(new QueuedClipboardRequest());
            _logger.Info($"Request enqueued. Queue depth: {_pendingRequests.Count}");
            EnsureProcessorRunning();
        }

        public HotkeyActionResult CopyLastResponseToClipboard()
        {
            _logger.Debug("invoked");

            if (string.IsNullOrEmpty(LastAiResponse))
            {
                _logger.Warn("CopyLastResponse: no previous AI response available.");
                return HotkeyActionResult.Fail("No previous AI response.");
            }

            _logger.Debug($"last response length={LastAiResponse.Length}");

            ClipboardWriteResult write = _clipboardService.TryWriteText(LastAiResponse);
            if (write.Success)
            {
                _logger.Info("CopyLastResponse: last response copied to clipboard.");
                return HotkeyActionResult.Ok();
            }
            else
            {
                _logger.Error($"CopyLastResponse: clipboard write failed: {write.Error}");
                return HotkeyActionResult.Fail(write.Error);
            }
        }

        public void StartOcrSelection()
        {
            if (_ocrClient == null)
            {
                _logger.Warn("OCR client not available.");
                _notifications.NotifyFailure();
                return;
            }

            if (_mouseHook == null)
            {
                _logger.Warn("Mouse hook not available.");
                _notifications.NotifyFailure();
                return;
            }

            _mouseHook.StartSelection();
        }

        public void CancelOcrSelection()
        {
            if (_mouseHook != null)
            {
                _mouseHook.CancelSelection();
            }
        }

        public void RefreshOcrClient(IOcrClient client)
        {
            _ocrClient = client;
            _logger.Info("OCR client refreshed.");
        }

        public void RefreshConfiguration()
        {
            AppConfig config = _configManager.Current;
            _logger.Debug($"systemPrompt={config.SystemPrompt?.Length ?? 0} chars, historyLimit={config.HistoryLimit}");
            _chatHistory.UpdateConfiguration(config.SystemPrompt, config.HistoryLimit);
            _logger.Info("Controller configuration refreshed.");
        }

        // ----- Selection event handlers -----

        private void OnSelectionComplete(object sender, System.Drawing.Rectangle region)
        {
            _logger.Info($"Region selected: {region}");

            if (_ocrClient == null)
            {
                _logger.Warn("OCR client not available.");
                _notifications.NotifyFailure();
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await ExecuteScreenOcrAsync(region, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error("Unexpected error during screen OCR.", ex);
                    _notifications.NotifyFailure();
                }
            });
        }

        private void OnSelectionCancelled(object sender, EventArgs e)
        {
            _logger.Info("Selection cancelled by user.");
            _notifications.NotifyFailure();
        }

        private async Task ExecuteScreenOcrAsync(System.Drawing.Rectangle region, CancellationToken cancellationToken)
        {
            _logger.Info("Capturing screen region.");

            byte[] imageData = _screenCapture.CaptureRegion(region);
            if (imageData == null || imageData.Length == 0)
            {
                _logger.Error("Screen capture failed: empty image.");
                _notifications.NotifyFailure();
                return;
            }

            _logger.Info($"Screen captured ({imageData.Length} bytes).");

            AppConfig config = _configManager.Current;
            string prompt = config.Ocr?.Prompt;

            if (config.Ocr?.UseClipboardPrompt == true)
            {
                var clipboardResult = _clipboardService.TryReadText();
                if (clipboardResult.Success && !string.IsNullOrWhiteSpace(clipboardResult.Text))
                {
                    prompt = clipboardResult.Text;
                    _logger.Debug("Using clipboard text as OCR prompt");
                }
            }

            _logger.Debug("calling OCR client");
            AiSingleAttemptResult ocrResult = await _ocrClient.RunOcrAsync(
                imageData,
                prompt,
                config.Ocr?.TimeoutMs ?? 60000,
                cancellationToken).ConfigureAwait(false);

            if (!ocrResult.Success)
            {
                _logger.Error($"OCR failed: {ocrResult.Error}");
                _notifications.NotifyFailure();
                return;
            }

            LastAiResponse = ocrResult.Content;
            _logger.Info($"OCR completed. Press {config.CopyLastResponseHotkey} to copy to clipboard.");
            _notifications.NotifySuccess();
        }

        // ----- Internal queue processor -----

        private void EnsureProcessorRunning()
        {
            if (Interlocked.CompareExchange(ref _processorRunning, 1, 0) == 0)
            {
                _logger.Debug("starting background task");
                Task.Run(() => ProcessQueueAsync(_cts.Token));
            }
            else
            {
                _logger.Debug("processor already running");
            }
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            _logger.Debug("started");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!_pendingRequests.TryDequeue(out QueuedClipboardRequest _))
                    {
                        _logger.Debug("queue empty, exiting");
                        break;
                    }

                    _logger.Debug("dequeued request, processing");

                    try
                    {
                        await ExecuteClipboardSendAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Debug("operation cancelled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Unexpected error during clipboard send.", ex);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _processorRunning, 0);
                _logger.Debug("processor stopped");

                if (!_pendingRequests.IsEmpty && !cancellationToken.IsCancellationRequested)
                {
                    _logger.Debug("new items arrived while shutting down, restarting");
                    EnsureProcessorRunning();
                }
            }
        }

        private async Task ExecuteClipboardSendAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Processing clipboard send request.");

            _logger.Debug("reading clipboard");
            ClipboardReadResult read = _clipboardService.TryReadText();
            if (!read.Success)
            {
                _logger.Warn($"Clipboard read failed: {read.Error}");
                _notifications.NotifyFailure();
                return;
            }

            string userText = read.Text;
            _logger.Info($"Clipboard read OK ({userText.Length} chars).");

            _logger.Debug("building chat messages");
            IReadOnlyList<ChatMessage> messages =
                _chatHistory.BuildRequestMessages(userText);
            _logger.Debug($"built {messages.Count} messages");

            _logger.Debug("calling AI client");
            AiResponseResult aiResult = await _aiClient.SendAsync(
                _configManager.Current, messages, cancellationToken).ConfigureAwait(false);

            if (!aiResult.Success)
            {
                _logger.Error($"AI request failed: {aiResult.Error}");
                _notifications.NotifyFailure();
                return;
            }

            _chatHistory.AppendSuccessfulTurn(userText, aiResult.Content);
            LastAiResponse = aiResult.Content;

            _logger.Info($"AI response received via model {aiResult.ModelUsed}. Press {_configManager.Current.CopyLastResponseHotkey} to copy to clipboard.");
            _notifications.NotifySuccess();
        }

        public void Dispose()
        {
            _logger.Debug("cancelling token source");
            _cts.Cancel();
            _cts.Dispose();

            if (_mouseHook != null)
            {
                _mouseHook.SelectionComplete -= OnSelectionComplete;
                _mouseHook.SelectionCancelled -= OnSelectionCancelled;
            }
        }
    }
}