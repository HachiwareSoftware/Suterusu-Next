using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Suterusu.Services;
using Xunit;

namespace Suterusu.Tests
{
    public class OcrPromptRequestTests
    {
        [Fact]
        public async Task LlamaCppOcrClient_SendsOcrPromptAsSystemMessage()
        {
            var handler = new RecordingHandler("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");
            var client = new LlamaCppOcrClient(new NLogLogger("Suterusu.Tests.OCR"), "http://localhost:8080", "model", 128, handler);

            await client.RunOcrAsync(new byte[] { 1 }, "OCR system prompt", 30000, CancellationToken.None);

            var body = JObject.Parse(handler.LastRequestBody);
            Assert.Equal("system", body["messages"]?[0]?["role"]?.ToString());
            Assert.Equal("OCR system prompt", body["messages"]?[0]?["content"]?.ToString());
            Assert.Equal("user", body["messages"]?[1]?["role"]?.ToString());
        }

        [Fact]
        public async Task ZaiOcrClient_SendsOcrPromptField()
        {
            var handler = new RecordingHandler("{\"text\":\"ok\"}");
            var client = new ZaiOcrClient(new NLogLogger("Suterusu.Tests.OCR"), "token", "model", handler);

            await client.RunOcrAsync(new byte[] { 1 }, "OCR system prompt", 30000, CancellationToken.None);

            var body = JObject.Parse(handler.LastRequestBody);
            Assert.Equal("OCR system prompt", body["prompt"]?.ToString());
        }

        [Fact]
        public async Task HuggingFaceOcrClient_SendsOcrPromptField()
        {
            var handler = new RecordingHandler("[\"ok\"]");
            var client = new HuggingFaceOcrClient(new NLogLogger("Suterusu.Tests.OCR"), "https://api.huggingface.co/v1", "token", "model", handler);

            await client.RunOcrAsync(new byte[] { 1 }, "OCR system prompt", 30000, CancellationToken.None);

            var body = JObject.Parse(handler.LastRequestBody);
            Assert.Equal("OCR system prompt", body["prompt"]?.ToString());
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly string _responseBody;

            public RecordingHandler(string responseBody)
            {
                _responseBody = responseBody;
            }

            public string LastRequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseBody)
                };
            }
        }
    }
}
