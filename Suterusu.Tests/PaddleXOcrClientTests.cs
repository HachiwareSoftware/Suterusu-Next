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
    public class PaddleXOcrClientTests
    {
        [Fact]
        public async Task RunOcrAsync_PostsImageToOcrEndpoint()
        {
            var handler = new RecordingHandler("{\"errorCode\":0,\"result\":{\"ocrResults\":[{\"prunedResult\":{\"rec_texts\":[\"hello\",\"world\"]}}]}}");
            var client = new PaddleXOcrClient(new NLogLogger("Suterusu.Tests.PaddleX"), "http://localhost:8080/", handler);

            var result = await client.RunOcrAsync(new byte[] { 1, 2, 3 }, null, 30000, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("hello" + Environment.NewLine + "world", result.Content);
            Assert.Equal("http://localhost:8080/ocr", handler.LastRequestUri.AbsoluteUri);

            var body = JObject.Parse(handler.LastRequestBody);
            Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), body["file"]?.ToString());
            Assert.Equal(1, body["fileType"]?.Value<int>());
            Assert.False(body["visualize"]?.Value<bool>() ?? true);
        }

        [Fact]
        public async Task RunOcrAsync_ReadsCamelCaseRecTextsFallback()
        {
            var handler = new RecordingHandler("{\"errorCode\":0,\"result\":{\"ocrResults\":[{\"prunedResult\":{\"recTexts\":[\"fallback\"]}}]}}");
            var client = new PaddleXOcrClient(new NLogLogger("Suterusu.Tests.PaddleX"), "http://localhost:8080", handler);

            var result = await client.RunOcrAsync(new byte[] { 1 }, null, 30000, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("fallback", result.Content);
        }

        [Fact]
        public async Task RunOcrAsync_ReturnsPaddleXErrorMessage()
        {
            var handler = new RecordingHandler("{\"errorCode\":12,\"errorMsg\":\"bad image\"}");
            var client = new PaddleXOcrClient(new NLogLogger("Suterusu.Tests.PaddleX"), "http://localhost:8080", handler);

            var result = await client.RunOcrAsync(new byte[] { 1 }, null, 30000, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("PaddleX OCR error: bad image", result.Error);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly string _responseBody;

            public RecordingHandler(string responseBody)
            {
                _responseBody = responseBody;
            }

            public Uri LastRequestUri { get; private set; }

            public string LastRequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestUri = request.RequestUri;
                LastRequestBody = await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseBody)
                };
            }
        }
    }
}
