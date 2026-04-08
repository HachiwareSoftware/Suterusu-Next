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

        private const int TimeoutSeconds = 60;

        public AiClient(ILogger logger)
        {
            _logger    = logger;
            _logger.Debug("initializing HTTP client");
            _httpClient = CreateHttpClient();
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
        }

        public async Task<AiResponseResult> SendAsync(
            AppConfig config,
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken)
        {
            _logger.Debug($"MultiRequestMode={config.MultiRequestMode}");

            if (config.MultiRequestMode == MultiRequestMode.Sequential)
            {
                _logger.Debug("Using sequential mode");
                return await SendSequentialAsync(config, messages, cancellationToken);
            }

            if (config.MultiRequestMode == MultiRequestMode.RoundRobin)
            {
                _logger.Debug("Using round-robin mode");
                return await SendRoundRobinAsync(config, messages, cancellationToken);
            }

            if (config.MultiRequestMode == MultiRequestMode.Fastest)
            {
                _logger.Debug("Using fastest mode");
                return await SendFastestAsync(config, messages, cancellationToken);
            }

            _logger.Debug("No recognized mode; defaulting to sequential");
            return await SendSequentialAsync(config, messages, cancellationToken);
        }

        private static List<EndpointConfig> GetEndpoints(AppConfig config)
        {
            return config.ModelPriority?.ConvertAll(e => e.ToEndpointConfig()) ?? new List<EndpointConfig>();
        }

        private async Task<AiResponseResult> SendSequentialAsync(
            AppConfig config,
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();
            var endpoints = GetEndpoints(config);

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

            string summary = "All endpoints failed: " + string.Join(
                "; ", errors);
            _logger.Error(summary);
            return AiResponseResult.Fail(summary);
        }

        private async Task<AiResponseResult> SendRoundRobinAsync(
            AppConfig config,
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startIndex = config.RoundRobinIndex;
            var endpoints = GetEndpoints(config);
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

            _logger.Error("All round-robin endpoints failed");
            return AiResponseResult.Fail("All round-robin endpoints failed.");
        }

        private async Task<AiResponseResult> SendFastestAsync(
            AppConfig config,
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (var raceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var raceToken = raceCts.Token;

                var tasks = new List<Task<AiEndpointAttemptResult>>();
                foreach (var endpoint in GetEndpoints(config))
                {
                    tasks.Add(SendToEndpointAsync(endpoint, config, messages, raceToken));
                }

                var remaining = new List<Task<AiEndpointAttemptResult>>(tasks);
                while (remaining.Count > 0)
                {
                    Task<AiEndpointAttemptResult> completedTask = await Task.WhenAny(remaining).ConfigureAwait(false);
                    remaining.Remove(completedTask);

                    AiEndpointAttemptResult result;
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
                        _logger.Info($"Fastest mode success with endpoint: {result.ModelUsed}");
                        return AiResponseResult.Ok(result.Content, result.ModelUsed);
                    }

                    _logger.Warn($"Fastest mode: endpoint failed ({result.Error}), waiting for next.");
                }

                _logger.Error("Fastest mode: all endpoints failed.");
                return AiResponseResult.Fail("Fastest mode: all endpoints failed.");
            }
        }

        private async Task<AiEndpointAttemptResult> SendToEndpointAsync(
            EndpointConfig endpoint,
            AppConfig config,
            IReadOnlyList<ChatMessage> messages,
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
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken)
        {
            try
            {
                var request = new ChatCompletionRequest
                {
                    Model    = model,
                    Messages = new List<ChatMessage>(messages)
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
                    using (HttpResponseMessage response = await _httpClient
                        .SendAsync(httpRequest, cancellationToken).ConfigureAwait(false))
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
            catch (TaskCanceledException)
            {
                _logger.Debug("request timed out");
                return AiSingleAttemptResult.Fail("Request timed out.");
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("operation cancelled");
                throw; // propagate cancellation
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
