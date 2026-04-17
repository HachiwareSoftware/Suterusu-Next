using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class HuggingFaceOcrClient : IOcrClient
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly string _token;
        private readonly string _model;

        public HuggingFaceOcrClient(ILogger logger, string token, string model, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _token = token;
            _model = string.IsNullOrWhiteSpace(model) ? "zai-org/GLM-OCR" : model;
            _httpClient = handler != null
                ? new HttpClient(handler)
                : new HttpClient { Timeout = TimeSpan.FromMilliseconds(0) };
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

                var request = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = prompt },
                                new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                            }
                        }
                    }
                };

                string json = JsonConvert.SerializeObject(request, JsonSettings.Compact);
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://router.huggingface.co/v1/chat/completions")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                httpRequest.Headers.Add("Authorization", "Bearer " + _token);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeoutMs);

                    var response = await _httpClient.SendAsync(httpRequest, cts.Token);
                    var responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return AiSingleAttemptResult.Fail($"HF API error: {response.StatusCode} - {responseJson}");
                    }

                    var chatResponse = JsonConvert.DeserializeObject<ChatCompletionResponse>(responseJson);
                    if (chatResponse?.Error != null)
                    {
                        return AiSingleAttemptResult.Fail($"HF API error: {chatResponse.Error.Message}");
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
                _logger.Error("HuggingFace OCR failed.", ex);
                return AiSingleAttemptResult.Fail($"OCR failed: {ex.Message}");
            }
        }
    }
}