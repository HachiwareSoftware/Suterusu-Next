using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public sealed class CdpClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _socketLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket;
        private int _messageId;

        public CdpClient(ILogger logger, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
        }

        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

        public async Task<bool> ConnectAsync(CdpSettings settings, CancellationToken cancellationToken)
        {
            Disconnect();

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(settings.ConnectTimeoutMs);

                var targets = await QueryTargetsAsync(settings.Port, timeoutCts.Token).ConfigureAwait(false);
                CdpTargetInfo target = SelectTarget(targets, settings.UrlPattern, _logger);
                if (target == null || string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
                    return false;

                _socket = new ClientWebSocket();
                await _socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), timeoutCts.Token).ConfigureAwait(false);
                _logger.Info("Connected to target: " + target.Title + " | " + target.Url);

                await SendCommandAsync("Page.enable", new JObject(), timeoutCts.Token).ConfigureAwait(false);
                return true;
            }
        }

        public async Task<IReadOnlyList<CdpTargetInfo>> QueryTargetsAsync(int port, CancellationToken cancellationToken)
        {
            using (var response = await _httpClient.GetAsync("http://127.0.0.1:" + port + "/json", cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseTargets(json);
            }
        }

        private static IReadOnlyList<CdpTargetInfo> ParseTargets(string json)
        {
            var entries = JArray.Parse(json);
            var targets = new List<CdpTargetInfo>();

            foreach (JToken entry in entries)
            {
                var target = new CdpTargetInfo
                {
                    Id = (string)entry["id"],
                    Title = (string)entry["title"],
                    Url = (string)entry["url"],
                    Type = (string)entry["type"],
                    WebSocketDebuggerUrl = (string)entry["webSocketDebuggerUrl"]
                };

                if (string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
                    targets.Add(target);
            }

            return targets;
        }

        public static CdpTargetInfo SelectTarget(IEnumerable<CdpTargetInfo> targets, string urlPattern, ILogger logger = null)
        {
            var pages = (targets ?? Enumerable.Empty<CdpTargetInfo>())
                .Where(t => string.Equals(t.Type, "page", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(t.WebSocketDebuggerUrl)
                    && IsInjectablePage(t.Url))
                .ToList();

            if (string.IsNullOrWhiteSpace(urlPattern))
                return pages.FirstOrDefault();

            try
            {
                var regex = new Regex(urlPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return pages.FirstOrDefault(t => regex.IsMatch(t.Url ?? ""));
            }
            catch (ArgumentException ex)
            {
                logger?.Warn("Invalid CDP URL pattern: " + ex.Message);
                return null;
            }
        }

        public async Task<bool> InjectPersistentScriptAsync(string scriptPath, CancellationToken cancellationToken)
        {
            return await InjectEventScriptAsync(scriptPath, "load", cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> InjectEventScriptAsync(string scriptPath, string eventName, CancellationToken cancellationToken)
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
                return false;

            string source = File.ReadAllText(scriptPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(source))
                return false;

            string expression = BuildEventExpression(source, eventName);

            var persistParams = new JObject { ["source"] = expression };
            JObject persistResponse = await SendCommandAsync("Page.addScriptToEvaluateOnNewDocument", persistParams, cancellationToken).ConfigureAwait(false);
            if (HasException(persistResponse))
                return false;

            var evalParams = new JObject
            {
                ["expression"] = expression,
                ["userGesture"] = false,
                ["awaitPromise"] = false,
                ["returnByValue"] = false
            };

            JObject evalResponse = await SendCommandAsync("Runtime.evaluate", evalParams, cancellationToken).ConfigureAwait(false);
            return !HasException(evalResponse);
        }

        public async Task<JObject> SendCommandAsync(string method, JObject parameters, CancellationToken cancellationToken)
        {
            if (!IsConnected)
                throw new InvalidOperationException("CDP socket is not connected.");

            await _socketLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int id = Interlocked.Increment(ref _messageId);
                var command = new JObject
                {
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters ?? new JObject()
                };

                byte[] data = Encoding.UTF8.GetBytes(command.ToString(Newtonsoft.Json.Formatting.None));
                await _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    JObject response = await ReceiveJsonAsync(cancellationToken).ConfigureAwait(false);
                    if ((int?)response["id"] == id)
                        return response;
                }
            }
            catch
            {
                Disconnect();
                throw;
            }
            finally
            {
                _socketLock.Release();
            }
        }

        public void Disconnect()
        {
            try
            {
                if (_socket != null && _socket.State == WebSocketState.Open)
                    _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None).Wait(500);
            }
            catch
            {
            }

            _socket?.Dispose();
            _socket = null;
        }

        public void Dispose()
        {
            Disconnect();
            _socketLock.Dispose();
            _httpClient.Dispose();
        }

        private static string BuildEventExpression(string source, string eventName)
        {
            eventName = string.IsNullOrWhiteSpace(eventName) ? "load" : eventName.Trim().ToLowerInvariant();
            string marker = "__suterusu_cdp_" + eventName + "_" + ComputeShortHash(source);
            return "(() => { try {\n"
                + "const marker = '" + marker + "';\n"
                + "if (window[marker]) return;\n"
                + "Object.defineProperty(window, marker, { value: true, configurable: false, enumerable: false });\n"
                + "const run = (event) => { try {\n"
                + source
                + "\n} catch (_) {} };\n"
                + BuildEventDispatchSource(eventName)
                + "\n} catch (_) {} })();";
        }

        private static string BuildEventDispatchSource(string eventName)
        {
            if (eventName == "connect")
                return "run();";

            if (eventName == "load")
            {
                return "if (document.readyState === 'complete' || document.readyState === 'interactive') { run(); }"
                    + " else { window.addEventListener('load', run, { once: true, capture: true }); }";
            }

            return "window.addEventListener('" + eventName + "', run, { capture: true });";
        }

        private static string ComputeShortHash(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                var builder = new StringBuilder(16);
                for (int i = 0; i < 8 && i < bytes.Length; i++)
                    builder.Append(bytes[i].ToString("x2"));

                return builder.ToString();
            }
        }

        private static bool HasException(JObject response)
        {
            return response?["result"]?["exceptionDetails"] != null || response?["error"] != null;
        }

        private static bool IsInjectablePage(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true;

            return !url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("chrome-devtools://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("edge://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<JObject> ReceiveJsonAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using (var stream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new WebSocketException("CDP socket closed.");

                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(stream.ToArray());
                return JObject.Parse(json);
            }
        }
    }
}
