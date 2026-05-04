using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class HuggingFaceOcrClient : IOcrClient
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _token;
        private readonly string _model;

        public HuggingFaceOcrClient(ILogger logger, string baseUrl, string token, string model, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _baseUrl = baseUrl?.TrimEnd('/') ?? "https://api.huggingface.co/v1";
            _token = token;
            _model = string.IsNullOrWhiteSpace(model) ? "google/ocr" : model;
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
                    prompt = string.IsNullOrWhiteSpace(prompt)
                        ? "Recognize all text from this image."
                        : prompt,
                    inputs = $"data:image/png;base64,{base64Image}"
                };
                var jsonBody = JsonConvert.SerializeObject(requestBody);

                var url = _baseUrl + "/vision/ocr";
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                httpRequest.Headers.Add("Authorization", "Bearer " + _token);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeoutMs > 0 ? timeoutMs : 60000);

                    var response = await _httpClient.SendAsync(httpRequest, cts.Token);
                    var responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return AiSingleAttemptResult.Fail($"HuggingFace API error: {response.StatusCode} - {responseJson}");
                    }

                    string content = null;

                    var json = JArray.Parse(responseJson);
                    if (json.Count > 0)
                    {
                        content = json[0]?.ToString();
                    }

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
