using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class LlamaCppModelsClient
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public LlamaCppModelsClient(ILogger logger, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _httpClient = handler != null
                ? new HttpClient(handler)
                : new HttpClient();
        }

        public async Task<List<string>> GetAvailableModelsAsync(
            string baseUrl,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            var models = new List<string>();

            try
            {
                var url = baseUrl.TrimEnd('/') + "/v1/models";
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(timeoutMs > 0 ? timeoutMs : 10000);

                    var response = await _httpClient.SendAsync(httpRequest, cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Warn($"Failed to fetch models: {response.StatusCode}");
                        return models;
                    }

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(responseJson);

                    var data = json["data"];
                    if (data != null && data.Type == JTokenType.Array)
                    {
                        foreach (var item in data)
                        {
                            var id = item["id"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(id))
                                models.Add(id);
                        }
                    }

                    if (models.Count == 0)
                    {
                        var modelList = json["models"];
                        if (modelList != null && modelList.Type == JTokenType.Array)
                        {
                            foreach (var item in modelList)
                            {
                                var name = item["name"]?.ToString() ?? item["model"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(name))
                                    models.Add(name);
                            }
                        }
                    }

                    _logger.Info($"Fetched {models.Count} models from llama.cpp");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Warn("Model fetch timed out.");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to fetch models: {ex.Message}");
            }

            return models;
        }
    }
}