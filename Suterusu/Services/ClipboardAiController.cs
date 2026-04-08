using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Suterusu.Configuration;
using Suterusu.Models;
using Suterusu.Notifications;

namespace Suterusu.Services
{
    /// <summary>
    /// Coordinates F6/F7/F8 runtime behavior.
    /// F7 enqueues clipboard send requests processed serially.
    /// F8 copies last AI response back to clipboard.
    /// F6 clears history.
    /// </summary>
    public class ClipboardAiController : IDisposable
    {
        private readonly ClipboardService    _clipboardService;
        private readonly AiClient            _aiClient;
        private readonly ChatHistory         _chatHistory;
        private readonly ConfigManager       _configManager;
        private readonly ILogger             _logger;
        private readonly INotificationService _notifications;

        private readonly ConcurrentQueue<QueuedClipboardRequest> _pendingRequests
            = new ConcurrentQueue<QueuedClipboardRequest>();

        private int  _processorRunning = 0; // 0=idle, 1=running (Interlocked flag)
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
            _logger.Debug("initialized");
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
                _logger.Warn("F8: no previous AI response available.");
                return HotkeyActionResult.Fail("No previous AI response.");
            }

            _logger.Debug($"last response length={LastAiResponse.Length}");

            ClipboardWriteResult write = _clipboardService.TryWriteText(LastAiResponse);
            if (write.Success)
            {
                _logger.Info("F8: last response copied to clipboard.");
                return HotkeyActionResult.Ok();
            }
            else
            {
                _logger.Error($"F8: clipboard write failed: {write.Error}");
                return HotkeyActionResult.Fail(write.Error);
            }
        }

        public void RefreshConfiguration()
        {
            AppConfig config = _configManager.Current;
            _logger.Debug($"systemPrompt={config.SystemPrompt?.Length ?? 0} chars, historyLimit={config.HistoryLimit}");
            _chatHistory.UpdateConfiguration(config.SystemPrompt, config.HistoryLimit);
            _logger.Info("Controller configuration refreshed.");
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
                        break; // queue drained
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

                // If new items arrived while we were shutting down, restart
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

            // Read clipboard
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

            // Build messages
            _logger.Debug("building chat messages");
            IReadOnlyList<ChatMessage> messages =
                _chatHistory.BuildRequestMessages(userText);
            _logger.Debug($"built {messages.Count} messages");

            // Call AI
            _logger.Debug("calling AI client");
            AiResponseResult aiResult = await _aiClient.SendAsync(
                _configManager.Current, messages, cancellationToken).ConfigureAwait(false);

            if (!aiResult.Success)
            {
                _logger.Error($"AI request failed: {aiResult.Error}");
                _notifications.NotifyFailure();
                return;
            }

            // Record last response (F8 will write to clipboard)
            _chatHistory.AppendSuccessfulTurn(userText, aiResult.Content);
            LastAiResponse = aiResult.Content;

            _logger.Info($"AI response received via model {aiResult.ModelUsed}. Press F8 to copy to clipboard.");
            _notifications.NotifySuccess();
        }

        public void Dispose()
        {
            _logger.Debug("cancelling token source");
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
