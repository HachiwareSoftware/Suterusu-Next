using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Suterusu.Configuration;
using Suterusu.Models;
using Suterusu.Services;
using Xunit;

namespace Suterusu.Tests
{
    public class AiClientTests
    {
        [Fact]
        public async Task SendAsync_UsesConfiguredTimeoutPerRequest()
        {
            var handler = new DelayedSuccessHandler(TimeSpan.FromMilliseconds(150));
            using (var client = new AiClient(new TestLogger(), handler))
            {
                var shortTimeoutConfig = CreateConfig(50);
                var longTimeoutConfig = CreateConfig(500);
                var messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = "hello" }
                };

                AiResponseResult shortTimeoutResult = await client.SendAsync(
                    shortTimeoutConfig,
                    messages,
                    CancellationToken.None);

                AiResponseResult longTimeoutResult = await client.SendAsync(
                    longTimeoutConfig,
                    messages,
                    CancellationToken.None);

                Assert.False(shortTimeoutResult.Success);
                Assert.Equal("All endpoints failed: [Slow endpoint] All models in endpoint failed.", shortTimeoutResult.Error);
                Assert.True(longTimeoutResult.Success);
                Assert.Equal("Delayed response", longTimeoutResult.Content);
                Assert.Equal("gpt-5.4-mini", longTimeoutResult.ModelUsed);
            }
        }

        private static AppConfig CreateConfig(int timeoutMs)
        {
            return new AppConfig
            {
                ModelPriority = new List<ModelEntry>
                {
                    new ModelEntry
                    {
                        Name = "Slow endpoint",
                        BaseUrl = "https://example.test/v1",
                        Model = "gpt-5.4-mini"
                    }
                },
                MultiRequestMode = MultiRequestMode.Sequential,
                MultiRequestTimeoutMs = timeoutMs,
                HistoryLimit = 10,
                SystemPrompt = "You are a helpful assistant."
            }.Normalize();
        }

        private sealed class DelayedSuccessHandler : HttpMessageHandler
        {
            private readonly TimeSpan _delay;

            public DelayedSuccessHandler(TimeSpan delay)
            {
                _delay = delay;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"choices\":[{\"message\":{\"content\":\"Delayed response\"}}]}",
                        Encoding.UTF8,
                        "application/json")
                };
            }
        }

        private sealed class TestLogger : ILogger
        {
            public void Debug(string message) { }
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception ex = null) { }
        }
    }
}
