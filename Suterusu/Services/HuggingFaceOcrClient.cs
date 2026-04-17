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
    public class HuggingFaceOcrResponse
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("parsed_text")]
        public string ParsedText { get; set; }
    }

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
                    file = $"data:image/png;base64,{base64Image}"
                };
                var jsonBody = JsonConvert.SerializeObject(requestBody);

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.z.ai/api/paas/v4/layout_parsing")
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                httpRequest.Headers.Add("Authorization", "Bearer " + _token);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeoutMs);

                    var response = await _httpClient.SendAsync(httpRequest, cts.Token);
                    var responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return AiSingleAttemptResult.Fail($"Z.ai API error: {response.StatusCode} - {responseJson}");
                    }

                    var hfResponse = JsonConvert.DeserializeObject<HuggingFaceOcrResponse>(responseJson);
                    string content = hfResponse?.Text ?? hfResponse?.ParsedText;
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
                _logger.Error("Z.ai OCR failed.", ex);
                return AiSingleAttemptResult.Fail($"OCR failed: {ex.Message}");
            }
        }
    }
}