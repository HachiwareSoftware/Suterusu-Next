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
            _logger.Debug($"entering with {messages.Count} messages, {config.Models?.Count ?? 0} models configured");

            if (config.Models == null || config.Models.Count == 0)
            {
                _logger.Error("no models configured");
                return AiResponseResult.Fail("No models configured.");
            }

            var errors = new List<string>();

            foreach (string model in config.Models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.Info($"Trying model: {model}");

                AiSingleAttemptResult attempt = await SendToModelAsync(
                    model, config, messages, cancellationToken).ConfigureAwait(false);

                if (attempt.Success)
                {
                    _logger.Info($"Success with model: {model}");
                    return AiResponseResult.Ok(attempt.Content, model);
                }

                _logger.Warn($"Model {model} failed: {attempt.Error}");
                errors.Add($"[{model}] {attempt.Error}");
            }

            string summary = "All models failed: " + string.Join("; ", errors);
            _logger.Error(summary);
            return AiResponseResult.Fail(summary);
        }

        private async Task<AiSingleAttemptResult> SendToModelAsync(
            string model,
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

                string baseUrl = config.ApiBaseUrl.TrimEnd('/');
                string url = baseUrl.EndsWith("/chat/completions") 
                    ? baseUrl 
                    : baseUrl + "/chat/completions";

                _logger.Debug($"target URL: {url}");

                using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    bool hasApiKey = !string.IsNullOrWhiteSpace(config.ApiKey);
                    _logger.Debug($"hasApiKey={hasApiKey}, key length={config.ApiKey?.Length ?? 0}");
                    if (hasApiKey)
                    {
                        httpRequest.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
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
}
