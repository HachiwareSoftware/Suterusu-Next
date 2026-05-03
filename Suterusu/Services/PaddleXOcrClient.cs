using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class PaddleXOcrClient : IOcrClient
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public PaddleXOcrClient(ILogger logger, string baseUrl, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://localhost:8080"
                : baseUrl.TrimEnd('/');
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
                var requestBody = new
                {
                    file = Convert.ToBase64String(imageData),
                    fileType = 1,
                    visualize = false
                };

                var jsonBody = JsonConvert.SerializeObject(requestBody);
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/ocr")
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeoutMs > 0 ? timeoutMs : 60000);

                    var response = await _httpClient.SendAsync(httpRequest, cts.Token);
                    var responseJson = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return AiSingleAttemptResult.Fail($"PaddleX OCR error: {response.StatusCode} - {responseJson}");

                    var json = JObject.Parse(responseJson);
                    int errorCode = json["errorCode"]?.Value<int>() ?? 0;
                    if (errorCode != 0)
                    {
                        string errorMessage = json["errorMsg"]?.ToString();
                        return AiSingleAttemptResult.Fail(string.IsNullOrWhiteSpace(errorMessage)
                            ? $"PaddleX OCR error code {errorCode}."
                            : $"PaddleX OCR error: {errorMessage}");
                    }

                    string text = ExtractText(json);
                    if (string.IsNullOrWhiteSpace(text))
                        return AiSingleAttemptResult.Fail("No OCR text in PaddleX response.");

                    return AiSingleAttemptResult.Ok(text);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AiSingleAttemptResult.Fail("OCR request timed out.");
            }
            catch (Exception ex)
            {
                _logger.Error("PaddleX OCR failed.", ex);
                return AiSingleAttemptResult.Fail($"OCR failed: {ex.Message}");
            }
        }

        private static string ExtractText(JObject json)
        {
            var lines = json["result"]?["ocrResults"]?
                .SelectMany(result => ReadTextArray(result?["prunedResult"]?["rec_texts"])
                    .Concat(ReadTextArray(result?["prunedResult"]?["recTexts"])))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return lines == null || lines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, lines);
        }

        private static string[] ReadTextArray(JToken token)
        {
            if (token == null || token.Type != JTokenType.Array)
                return new string[0];

            return token
                .Select(item => item?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
        }
    }
}
