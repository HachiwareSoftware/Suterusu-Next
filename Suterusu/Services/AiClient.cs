using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class AiClient : IDisposable
    {
        private readonly ILogger     _logger;
        private HttpClient           _httpClient;

        public AiClient(ILogger logger)
            : this(logger, null)
        {
        }

        public AiClient(ILogger logger, HttpMessageHandler handler)
        {
            _logger    = logger;
            _logger.Debug("initializing HTTP client");
            _httpClient = CreateHttpClient(handler);
        }

        private static HttpClient CreateHttpClient(HttpMessageHandler handler)
        {
            var httpClient = handler != null
                ? new HttpClient(handler)
                : new HttpClient();

            httpClient.Timeout = Timeout.InfiniteTimeSpan;
            return httpClient;
        }

        public async Task<AiResponseResult> SendAsync(
            AppConfig config,
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken)
        {
            var requestMessages = new List<ChatRequestMessage>();
            foreach (var message in messages)
                requestMessages.Add(ChatRequestMessage.FromChatMessage(message));

            return await SendRequestAsync(config, requestMessages, false, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AiResponseResult> SendVisionAsync(
            AppConfig config,
            IReadOnlyList<ChatRequestMessage> messages,
            CancellationToken cancellationToken)
        {
            return await SendRequestAsync(config, messages, true, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AiResponseResult> SendRequestAsync(
            AppConfig config,
            IReadOnlyList<ChatRequestMessage> messages,
            bool visionOnly,
            CancellationToken cancellationToken)
        {
            _logger.Debug($"MultiRequestMode={config.MultiRequestMode}");

            if (config.MultiRequestMode == MultiRequestMode.Sequential)
            {
                _logger.Debug("Using sequential mode");
                return await SendSequentialAsync(config, messages, visionOnly, cancellationToken);
            }

            if (config.MultiRequestMode == MultiRequestMode.RoundRobin)
            {
                _logger.Debug("Using round-robin mode");
                return await SendRoundRobinAsync(config, messages, visionOnly, cancellationToken);
            }

            if (config.MultiRequestMode == MultiRequestMode.Fastest)
            {
                _logger.Debug("Using fastest mode");
                return await SendFastestAsync(config, messages, visionOnly, cancellationToken);
            }

            _logger.Debug("No recognized mode; defaulting to sequential");
            return await SendSequentialAsync(config, messages, visionOnly, cancellationToken);
        }

        private static List<EndpointConfig> GetEndpoints(AppConfig config, bool visionOnly = false)
        {
            var endpoints = config.ModelPriority?.ConvertAll(e => e.ToEndpointConfig()) ?? new List<EndpointConfig>();
            return visionOnly
                ? endpoints.FindAll(e => e.Capability != ModelCapability.TextOnly)
                : endpoints;
        }

        private static AiResponseResult FailNoVisionModels()
            => AiResponseResult.Fail("No vision-capable model entries are configured.");

        private async Task<AiResponseResult> SendSequentialAsync(
            AppConfig config,
            IReadOnlyList<ChatRequestMessage> messages,
            bool visionOnly,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();
            var endpoints = GetEndpoints(config, visionOnly);

            if (visionOnly && endpoints.Count == 0)
                return FailNoVisionModels();

            foreach (var endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.Info($"Trying endpoint: {endpoint.Name}");

                var endpointResult = await SendToEndpointAsync(endpoint, config, messages, cancellationToken).ConfigureAwait(false);

                if (endpointResult.Success)
                {
                    _logger.Info($"Success with endpoint: {endpoint.Name}");
                    return AiResponseResult.Ok(endpointResult.Content, endpointResult.ModelUsed);
                }

                _logger.Warn($"Endpoint {endpoint.Name} failed: {endpointResult.Error}");
                errors.Add($"[{endpoint.Name}] {endpointResult.Error}");
            }

            string prefix = visionOnly ? "No vision-capable model succeeded. Last errors: " : "All endpoints failed: ";
            string summary = prefix + string.Join(
                "; ", errors);
            _logger.Error(summary);
            return AiResponseResult.Fail(summary);
        }

        private async Task<AiResponseResult> SendRoundRobinAsync(
            AppConfig config,
            IReadOnlyList<ChatRequestMessage> messages,
            bool visionOnly,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startIndex = config.RoundRobinIndex;
            var endpoints = GetEndpoints(config, visionOnly);
            if (visionOnly && endpoints.Count == 0)
                return FailNoVisionModels();
            int attempts = 0;

            while (attempts < endpoints.Count)
            {
                var endpointIndex = (startIndex + attempts) % endpoints.Count;
                var endpoint = endpoints[endpointIndex];

                _logger.Info($"Round-robin trying endpoint: {endpoint.Name}");

                var endpointResult = await SendToEndpointAsync(
                    endpoint, config, messages, cancellationToken).ConfigureAwait(false);

                if (endpointResult.Success)
                {
                    config.RoundRobinIndex = (endpointIndex + 1) % endpoints.Count;
                    _logger.Info($"Success with endpoint: {endpoint.Name}");
                    return AiResponseResult.Ok(endpointResult.Content, endpointResult.ModelUsed);
                }

                _logger.Warn($"Endpoint {endpoint.Name} failed: {endpointResult.Error}");
                attempts++;
            }

            string roundRobinError = visionOnly
                ? "No vision-capable model succeeded. Last errors: all eligible round-robin endpoints failed."
                : "All round-robin endpoints failed.";
            _logger.Error(roundRobinError);
            return AiResponseResult.Fail(roundRobinError);
        }

        private async Task<AiResponseResult> SendFastestAsync(
            AppConfig config,
            IReadOnlyList<ChatRequestMessage> messages,
            bool visionOnly,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (var raceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var raceToken = raceCts.Token;

                var allModelTasks = new List<Task<AiSingleAttemptResult>>();
                var modelNames = new List<string>();

                var endpoints = GetEndpoints(config, visionOnly);
                if (visionOnly && endpoints.Count == 0)
                    return FailNoVisionModels();

                foreach (var endpoint in endpoints)
                {
                    foreach (var model in endpoint.Models)
                    {
                        allModelTasks.Add(SendToModelAsync(model, endpoint, config, messages, raceToken));
                        modelNames.Add(model);
                    }
                }

                var remaining = new List<Task<AiSingleAttemptResult>>(allModelTasks);
                while (remaining.Count > 0)
                {
                    Task<AiSingleAttemptResult> completedTask = await Task.WhenAny(remaining).ConfigureAwait(false);
                    remaining.Remove(completedTask);

                    AiSingleAttemptResult result;
                    try
                    {
                        result = await completedTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Debug("Fastest mode: a request was cancelled");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Fastest mode: a request threw unexpectedly: {ex.Message}");
                        continue;
                    }

                    if (result.Success)
                    {
                        raceCts.Cancel();
                        var idx = allModelTasks.IndexOf(completedTask);
                        var modelName = idx >= 0 && idx < modelNames.Count ? modelNames[idx] : "unknown";
                        _logger.Info($"Fastest mode success with model: {modelName}");
                        return AiResponseResult.Ok(result.Content, modelName);
                    }

                    var failedIdx = allModelTasks.IndexOf(completedTask);
                    var failedModel = failedIdx >= 0 && failedIdx < modelNames.Count ? modelNames[failedIdx] : "unknown";
                    _logger.Warn($"Fastest mode: model {failedModel} failed ({result.Error}), waiting for next.");
                }

                string fastestError = visionOnly
                    ? "No vision-capable model succeeded. Last errors: fastest mode all eligible models failed."
                    : "Fastest mode: all models failed.";
                _logger.Error(fastestError);
                return AiResponseResult.Fail(fastestError);
            }
        }

        private async Task<AiEndpointAttemptResult> SendToEndpointAsync(
            EndpointConfig endpoint,
            AppConfig config,
            IReadOnlyList<ChatRequestMessage> messages,
            CancellationToken cancellationToken)
        {
            foreach (var model in endpoint.Models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.Info($"Trying model: {model}");

                var attempt = await SendToModelAsync(model, endpoint, config, messages, cancellationToken).ConfigureAwait(false);

                if (attempt.Success)
                {
                    return new AiEndpointAttemptResult(attempt.Content, model, true, null);
                }

                _logger.Warn($"Model {model} failed: {attempt.Error}");
            }

            return new AiEndpointAttemptResult(null, null, false, "All models in endpoint failed.");
        }

        private async Task<AiSingleAttemptResult> SendToModelAsync(
            string model,
            EndpointConfig endpoint,
            AppConfig config,
            IReadOnlyList<ChatRequestMessage> messages,
            CancellationToken cancellationToken)
        {
            CancellationTokenSource timeoutCts = null;

            try
            {
                var request = new ChatCompletionRequest
                {
                    Model    = model,
                    Messages = new List<ChatRequestMessage>(messages)
                };

                string json    = JsonSettings.SerializeCompact(request);
                _logger.Debug($"serialized request JSON: {json}");

                string baseUrl = endpoint.BaseUrl.TrimEnd('/');
                string url = baseUrl.EndsWith("/chat/completions") 
                    ? baseUrl 
                    : baseUrl + "/chat/completions";

                _logger.Debug($"target URL: {url}");

                using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    bool hasApiKey = !string.IsNullOrWhiteSpace(endpoint.ApiKey);
                    _logger.Debug($"hasApiKey={hasApiKey}, key length={endpoint.ApiKey?.Length ?? 0}");
                    if (hasApiKey)
                    {
                        httpRequest.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
                    }

                    _logger.Debug($"sending HTTP request to {url} with auth={hasApiKey}");
                    timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(config.MultiRequestTimeoutMs);

                    using (HttpResponseMessage response = await _httpClient
                        .SendAsync(httpRequest, timeoutCts.Token).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        _logger.Debug($"response status={(int)response.StatusCode}, body length={body.Length}");

                        if (!response.IsSuccessStatusCode)
                        {
                            string errMsg = $"HTTP {(int)response.StatusCode}";
                            try
                            {
                                var errResp = JsonSettings.Deserialize<ChatCompletionResponse>(body);
                                if (errResp?.Error?.Message != null)
                                    errMsg += $": {errResp.Error.Message}";
                            }
                            catch { /* ignore parse error */ }
                            _logger.Debug($"error response: {errMsg}");
                            return AiSingleAttemptResult.Fail(errMsg);
                        }

                        ChatCompletionResponse parsed;
                        try
                        {
                            parsed = JsonSettings.Deserialize<ChatCompletionResponse>(body);
                            _logger.Debug($"parsed response, choices count={parsed?.Choices?.Count ?? 0}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"JSON parse error: {ex.Message}");
                            return AiSingleAttemptResult.Fail($"JSON parse error: {ex.Message}");
                        }

                        if (parsed?.Choices == null || parsed.Choices.Count == 0)
                        {
                            _logger.Debug("response has no choices");
                            return AiSingleAttemptResult.Fail("Response has no choices.");
                        }

                        string content = parsed.Choices[0]?.Message?.Content;
                        if (string.IsNullOrEmpty(content))
                        {
                            _logger.Debug("assistant returned empty content");
                            return AiSingleAttemptResult.Fail("Assistant returned empty content.");
                        }

                        _logger.Debug($"success, content length={content.Length}");
                        return AiSingleAttemptResult.Ok(content);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Debug("operation cancelled");
                    throw; // propagate cancellation (race lost or user cancel)
                }
                _logger.Debug("request timed out");
                return AiSingleAttemptResult.Fail("Request timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.Debug($"network error: {ex.Message}");
                return AiSingleAttemptResult.Fail($"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Debug($"unexpected error: {ex.Message}");
                return AiSingleAttemptResult.Fail($"Unexpected error: {ex.Message}");
            }
            finally
            {
                timeoutCts?.Dispose();
            }
        }

        public void Dispose()
        {
            _logger.Debug("disposing HTTP client");
            _httpClient?.Dispose();
            _httpClient = null;
        }
    }

    public sealed class AiEndpointAttemptResult
    {
        public string Content { get; }
        public string ModelUsed { get; }
        public bool Success { get; }
        public string Error { get; }

        public AiEndpointAttemptResult(string content, string modelUsed, bool success, string error)
        {
            Content = content;
            ModelUsed = modelUsed;
            Success = success;
            Error = error;
        }

        public static AiEndpointAttemptResult Ok(string content, string modelUsed)
        {
            return new AiEndpointAttemptResult(content, modelUsed, true, null);
        }

        public static AiEndpointAttemptResult Fail(string error)
        {
            return new AiEndpointAttemptResult(null, null, false, error);
        }
    }
}
