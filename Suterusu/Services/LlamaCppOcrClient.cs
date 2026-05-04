using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class LlamaCppOcrClient : IOcrClient
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly int _maxTokens;

        public LlamaCppOcrClient(ILogger logger, string baseUrl, string model, int maxTokens = 4096, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _baseUrl = baseUrl.TrimEnd('/');
            _model = string.IsNullOrWhiteSpace(model) ? "default" : model;
            _maxTokens = maxTokens > 0 ? maxTokens : 4096;
            _httpClient = handler != null
                ? new HttpClient(handler)
                : new HttpClient();
        }

        public async Task<AiSingleAttemptResult> RunOcrAsync(
            byte[] imageData,
            string prompt,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            try
            {
                string base64Image = Convert.ToBase64String(imageData);

                string url = _baseUrl;
                if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                {
                    url += "/v1/chat/completions";
                }

                var request = new
                {
                    model = _model,
                    max_tokens = _maxTokens,
                    messages = new object[]
                    {
                        new
                        {
                            role = "system",
                            content = string.IsNullOrWhiteSpace(prompt)
                                ? "Recognize all text from this image."
                                : prompt
                        },
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                            }
                        }
                    }
                };

                string json = JsonConvert.SerializeObject(request, JsonSettings.Compact);
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeoutMs);

                    var response = await _httpClient.SendAsync(httpRequest, cts.Token);
                    var responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return AiSingleAttemptResult.Fail($"llama.cpp API error: {response.StatusCode} - {responseJson}");
                    }

                    var chatResponse = JsonConvert.DeserializeObject<ChatCompletionResponse>(responseJson);
                    if (chatResponse?.Error != null)
                    {
                        return AiSingleAttemptResult.Fail($"llama.cpp API error: {chatResponse.Error.Message}");
                    }

                    string content = chatResponse?.Choices?[0]?.Message?.Content;
                    if (string.IsNullOrEmpty(content))
                    {
                        return AiSingleAttemptResult.Fail("No content in response.");
                    }

                    return AiSingleAttemptResult.Ok(content);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AiSingleAttemptResult.Fail("OCR request timed out.");
            }
            catch (Exception ex)
            {
                _logger.Error("LlamaCpp OCR failed.", ex);
                return AiSingleAttemptResult.Fail($"OCR failed: {ex.Message}");
            }
        }
    }
}
