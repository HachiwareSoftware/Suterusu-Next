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
        private readonly Dictionary<string, TargetConnection> _connections = new Dictionary<string, TargetConnection>(StringComparer.OrdinalIgnoreCase);
        private int _connectionGeneration;

        public CdpClient(ILogger logger, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
        }

        public bool IsConnected => _connections.Values.Any(c => c.IsConnected);

        public int ConnectionGeneration => _connectionGeneration;

        public async Task<bool> ConnectAsync(CdpSettings settings, CancellationToken cancellationToken)
        {
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(settings.ConnectTimeoutMs);

                var targets = await QueryTargetsAsync(settings.Port, timeoutCts.Token).ConfigureAwait(false);
                var selectedTargets = SelectTargets(targets, settings.UrlPattern, _logger).ToList();
                if (selectedTargets.Count == 0)
                {
                    Disconnect();
                    return false;
                }

                DisconnectStaleTargets(selectedTargets);

                foreach (CdpTargetInfo target in selectedTargets)
                {
                    if (string.IsNullOrWhiteSpace(target.Id) || _connections.ContainsKey(target.Id))
                        continue;

                    var connection = new TargetConnection(target);
                    await connection.Socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), timeoutCts.Token).ConfigureAwait(false);
                    _connections[target.Id] = connection;
                    _connectionGeneration++;
                    _logger.Info("Connected to target: " + target.Title + " | " + target.Url);

                    await SendCommandAsync(connection, "Page.enable", new JObject(), timeoutCts.Token).ConfigureAwait(false);
                }

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
            return SelectTargets(targets, urlPattern, logger).FirstOrDefault();
        }

        public static IReadOnlyList<CdpTargetInfo> SelectTargets(IEnumerable<CdpTargetInfo> targets, string urlPattern, ILogger logger = null)
        {
            var pages = (targets ?? Enumerable.Empty<CdpTargetInfo>())
                .Where(t => string.Equals(t.Type, "page", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(t.Id)
                    && !string.IsNullOrWhiteSpace(t.WebSocketDebuggerUrl)
                    && IsInjectablePage(t.Url))
                .ToList();

            if (string.IsNullOrWhiteSpace(urlPattern))
                return pages;

            try
            {
                var regex = new Regex(urlPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return pages.Where(t => regex.IsMatch(t.Url ?? "")).ToList();
            }
            catch (ArgumentException ex)
            {
                logger?.Warn("Invalid CDP URL pattern: " + ex.Message);
                return new List<CdpTargetInfo>();
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
            string scriptKey = BuildPersistentScriptKey(scriptPath, eventName);
            bool injectedAny = false;

            foreach (TargetConnection connection in _connections.Values.ToList())
            {
                if (!connection.IsConnected)
                    continue;

                string previousIdentifier;
                if (connection.PersistentScriptIds.TryGetValue(scriptKey, out previousIdentifier))
                {
                    await RemovePersistentScriptAsync(connection, previousIdentifier, cancellationToken).ConfigureAwait(false);
                    connection.PersistentScriptIds.Remove(scriptKey);
                }

                var persistParams = new JObject { ["source"] = expression };
                JObject persistResponse = await SendCommandAsync(connection, "Page.addScriptToEvaluateOnNewDocument", persistParams, cancellationToken).ConfigureAwait(false);
                if (HasException(persistResponse))
                    continue;

                string identifier = (string)persistResponse["result"]?["identifier"];
                if (!string.IsNullOrWhiteSpace(identifier))
                    connection.PersistentScriptIds[scriptKey] = identifier;

                var evalParams = new JObject
                {
                    ["expression"] = expression,
                    ["userGesture"] = false,
                    ["awaitPromise"] = false,
                    ["returnByValue"] = false
                };

                JObject evalResponse = await SendCommandAsync(connection, "Runtime.evaluate", evalParams, cancellationToken).ConfigureAwait(false);
                injectedAny = injectedAny || !HasException(evalResponse);
            }

            return injectedAny;
        }

        public async Task<JObject> SendCommandAsync(string method, JObject parameters, CancellationToken cancellationToken)
        {
            if (!IsConnected)
                throw new InvalidOperationException("CDP socket is not connected.");

            TargetConnection connection = _connections.Values.FirstOrDefault(c => c.IsConnected);
            if (connection == null)
                throw new InvalidOperationException("CDP socket is not connected.");

            return await SendCommandAsync(connection, method, parameters, cancellationToken).ConfigureAwait(false);
        }

        private async Task<JObject> SendCommandAsync(TargetConnection connection, string method, JObject parameters, CancellationToken cancellationToken)
        {
            if (connection == null || !connection.IsConnected)
                throw new InvalidOperationException("CDP socket is not connected.");

            await connection.SocketLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                int id = Interlocked.Increment(ref connection.MessageId);
                var command = new JObject
                {
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters ?? new JObject()
                };

                byte[] data = Encoding.UTF8.GetBytes(command.ToString(Newtonsoft.Json.Formatting.None));
                await connection.Socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    JObject response = await ReceiveJsonAsync(connection, cancellationToken).ConfigureAwait(false);
                    if ((int?)response["id"] == id)
                        return response;
                }
            }
            catch
            {
                Disconnect(connection.Target.Id);
                throw;
            }
            finally
            {
                connection.SocketLock.Release();
            }
        }

        public void Disconnect()
        {
            foreach (string targetId in _connections.Keys.ToList())
                Disconnect(targetId);
        }

        public void Dispose()
        {
            Disconnect();
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
                + source
                + "\n} catch (_) {} })();";
        }

        private async Task RemovePersistentScriptAsync(TargetConnection connection, string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return;

            var parameters = new JObject { ["identifier"] = identifier };
            JObject response = await SendCommandAsync(connection, "Page.removeScriptToEvaluateOnNewDocument", parameters, cancellationToken).ConfigureAwait(false);
            if (HasException(response))
                _logger.Warn("Failed to remove previous CDP script registration: " + identifier);
        }

        private static string BuildPersistentScriptKey(string scriptPath, string eventName)
        {
            string fullPath = Path.GetFullPath(scriptPath ?? "");
            return (eventName ?? "load").Trim().ToLowerInvariant() + "|" + fullPath;
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

        private void DisconnectStaleTargets(IReadOnlyList<CdpTargetInfo> selectedTargets)
        {
            var selectedIds = new HashSet<string>(selectedTargets.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
            foreach (string targetId in _connections.Keys.ToList())
            {
                if (!selectedIds.Contains(targetId) || !_connections[targetId].IsConnected)
                    Disconnect(targetId);
            }
        }

        private void Disconnect(string targetId)
        {
            TargetConnection connection;
            if (string.IsNullOrWhiteSpace(targetId) || !_connections.TryGetValue(targetId, out connection))
                return;

            try
            {
                if (connection.Socket.State == WebSocketState.Open)
                    connection.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None).Wait(500);
            }
            catch
            {
            }

            connection.Dispose();
            _connections.Remove(targetId);
            _connectionGeneration++;
        }

        private async Task<JObject> ReceiveJsonAsync(TargetConnection connection, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using (var stream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await connection.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new WebSocketException("CDP socket closed.");

                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(stream.ToArray());
                return JObject.Parse(json);
            }
        }

        private sealed class TargetConnection : IDisposable
        {
            public TargetConnection(CdpTargetInfo target)
            {
                Target = target;
                Socket = new ClientWebSocket();
                SocketLock = new SemaphoreSlim(1, 1);
                PersistentScriptIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public CdpTargetInfo Target { get; }

            public ClientWebSocket Socket { get; }

            public SemaphoreSlim SocketLock { get; }

            public Dictionary<string, string> PersistentScriptIds { get; }

            public int MessageId;

            public bool IsConnected => Socket != null && Socket.State == WebSocketState.Open;

            public void Dispose()
            {
                Socket.Dispose();
                SocketLock.Dispose();
                PersistentScriptIds.Clear();
            }
        }
    }
}
