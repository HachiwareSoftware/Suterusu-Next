using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class CustomOcrClient : IOcrClient
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _maxTokens;

        public CustomOcrClient(ILogger logger, string baseUrl, string apiKey, string model, int maxTokens = 4096, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
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
                var base64Image = Convert.ToBase64String(imageData);
                var requestBody = new
                {
                    model = _model,
                    messages = new object[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = prompt ?? "Recognize all text from this image." },
                                new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                            }
                        }
                    },
                    max_tokens = _maxTokens
                };
                var jsonBody = JsonConvert.SerializeObject(requestBody);

                var url = _baseUrl + "/v1/chat/completions";
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                httpRequest.Headers.Add("Authorization", "Bearer " + _apiKey);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeoutMs > 0 ? timeoutMs : 60000);

                    var response = await _httpClient.SendAsync(httpRequest, cts.Token);
                    var responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return AiSingleAttemptResult.Fail($"Custom API error: {response.StatusCode} - {responseJson}");
                    }

                    var json = JObject.Parse(responseJson);
                    var content = json["choices"]?[0]?["message"]?["content"]?.ToString();

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
                _logger.Error("Custom OCR failed.", ex);
                return AiSingleAttemptResult.Fail($"OCR failed: {ex.Message}");
            }
        }
    }
}