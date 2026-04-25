using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class CliProxyHttpClient : IDisposable
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public CliProxyHttpClient(ILogger logger)
            : this(logger, null)
        {
        }

        public CliProxyHttpClient(ILogger logger, HttpMessageHandler handler)
        {
            _logger = logger;
            _httpClient = handler != null ? new HttpClient(handler) : new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<CliProxyResult> WaitUntilReadyAsync(AppConfig config, int timeoutMs, CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            if (timeoutMs <= 0)
                timeoutMs = 15000;

            var startedAt = DateTime.UtcNow;
            Exception lastError = null;

            while ((DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var models = await GetModelsAsync(config, cancellationToken).ConfigureAwait(false);
                    if (models.Success)
                        return CliProxyResult.Ok();

                    lastError = new InvalidOperationException(models.Error ?? "CLI proxy is not ready.");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            return CliProxyResult.Fail(lastError?.Message ?? "CLI proxy did not become ready in time.");
        }

        public async Task<CliProxyHealthResult> GetModelsAsync(AppConfig config, CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return CliProxyHealthResult.Fail("CLI proxy settings are not configured.");

            var settings = config.CliProxy;
            var url = BuildUrl(settings, "/v1/models");

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    ApplyAuthorization(settings, request);
                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                            return CliProxyHealthResult.Fail($"HTTP {(int)response.StatusCode}: {Truncate(body)}");

                        var models = ParseModelIds(body);
                        return CliProxyHealthResult.Ok(models);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"CLI proxy models check failed: {ex.Message}");
                return CliProxyHealthResult.Fail(ex.Message);
            }
        }

        public async Task<CliProxyResult> TestChatCompletionAsync(AppConfig config, string model, CancellationToken cancellationToken)
        {
            if (config?.CliProxy == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            var settings = config.CliProxy;
            string modelName = string.IsNullOrWhiteSpace(model) ? settings.Model : model;

            if (string.IsNullOrWhiteSpace(modelName))
                return CliProxyResult.Fail("Model name is empty.");

            var payload = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "user", content = "Reply with OK." }
                }
            };

            var url = BuildUrl(settings, "/v1/chat/completions");

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    ApplyAuthorization(settings, request);
                    string json = JsonConvert.SerializeObject(payload);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                            return CliProxyResult.Fail($"HTTP {(int)response.StatusCode}: {Truncate(body)}");

                        try
                        {
                            var parsed = JsonSettings.Deserialize<ChatCompletionResponse>(body);
                            string content = parsed?.Choices != null && parsed.Choices.Count > 0
                                ? parsed.Choices[0]?.Message?.Content
                                : null;

                            if (string.IsNullOrWhiteSpace(content))
                                return CliProxyResult.Fail("Model test succeeded but returned empty content.");
                        }
                        catch (Exception ex)
                        {
                            return CliProxyResult.Fail("Model test parse failed: " + ex.Message);
                        }

                        return CliProxyResult.Ok();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"CLI proxy model test failed: {ex.Message}");
                return CliProxyResult.Fail(ex.Message);
            }
        }

        private static string BuildUrl(CliProxySettings settings, string path)
        {
            string host = string.IsNullOrWhiteSpace(settings.Host) ? "127.0.0.1" : settings.Host.Trim();
            string normalizedPath = path.StartsWith("/") ? path : "/" + path;
            return $"http://{host}:{settings.Port}{normalizedPath}";
        }

        private static void ApplyAuthorization(CliProxySettings settings, HttpRequestMessage request)
        {
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            }
        }

        private static IReadOnlyList<string> ParseModelIds(string body)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(body))
                return result;

            var root = JObject.Parse(body);
            var data = root["data"] as JArray;
            if (data == null)
                return result;

            foreach (var token in data)
            {
                string id = token?["id"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(id);
            }

            return result;
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (text.Length <= 300)
                return text;

            return text.Substring(0, 300) + "...";
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
