using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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

        [Fact]
        public async Task SendVisionAsync_SerializesImageUrlContent()
        {
            var handler = new CapturingHandler((request, call) => Success("vision response"));
            using (var client = new AiClient(new TestLogger(), handler))
            {
                var config = CreateVisionConfig();
                var messages = new List<ChatRequestMessage>
                {
                    new ChatRequestMessage("system", "sys"),
                    new ChatRequestMessage("user", new object[]
                    {
                        ChatMessageContentPart.TextPart("read this"),
                        ChatMessageContentPart.ImageUrlPart("data:image/png;base64,abc")
                    })
                };

                AiResponseResult result = await client.SendVisionAsync(config, messages, CancellationToken.None);

                Assert.True(result.Success);
                JObject json = JObject.Parse(handler.Bodies[0]);
                Assert.Equal("vision-model", json["model"]?.ToString());
                Assert.Equal("read this", json["messages"]?[1]?["content"]?[0]?["text"]?.ToString());
                Assert.Equal("data:image/png;base64,abc", json["messages"]?[1]?["content"]?[1]?["image_url"]?["url"]?.ToString());
            }
        }

        [Fact]
        public async Task SendVisionAsync_SkipsTextOnlyEntries()
        {
            var handler = new CapturingHandler((request, call) => Success("unused"));
            using (var client = new AiClient(new TestLogger(), handler))
            {
                var config = CreateVisionConfig();
                config.ModelPriority[0].Capability = ModelCapability.TextOnly;

                AiResponseResult result = await client.SendVisionAsync(
                    config,
                    new List<ChatRequestMessage> { new ChatRequestMessage("user", "x") },
                    CancellationToken.None);

                Assert.False(result.Success);
                Assert.Equal("No vision-capable model entries are configured.", result.Error);
                Assert.Empty(handler.Bodies);
            }
        }

        [Fact]
        public async Task SendVisionAsync_FallsBackFromAutoToVisionEntry()
        {
            var handler = new CapturingHandler((request, call) => call == 1
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":{\"message\":\"model does not support image input\"}}", Encoding.UTF8, "application/json")
                }
                : Success("vision response"));

            using (var client = new AiClient(new TestLogger(), handler))
            {
                var config = CreateVisionConfig();
                config.ModelPriority.Insert(0, new ModelEntry
                {
                    Name = "Auto endpoint",
                    BaseUrl = "https://auto.test/v1",
                    Model = "auto-model",
                    Capability = ModelCapability.Auto
                });

                AiResponseResult result = await client.SendVisionAsync(
                    config,
                    new List<ChatRequestMessage> { new ChatRequestMessage("user", "x") },
                    CancellationToken.None);

                Assert.True(result.Success);
                Assert.Equal("vision-model", result.ModelUsed);
                Assert.Equal(2, handler.Bodies.Count);
            }
        }

        [Fact]
        public async Task SendAsync_SerializesNonDefaultReasoningEffort()
        {
            var handler = new CapturingHandler((request, call) => Success("reasoned"));
            using (var client = new AiClient(new TestLogger(), handler))
            {
                var config = CreateConfig(500);
                config.ModelPriority[0].ReasoningEffort = "high";
                var messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = "hello" }
                };

                AiResponseResult result = await client.SendAsync(config, messages, CancellationToken.None);

                Assert.True(result.Success);
                JObject json = JObject.Parse(handler.Bodies[0]);
                Assert.Equal("high", json["reasoning_effort"]?.ToString());
            }
        }

        [Fact]
        public async Task SendAsync_OmitsDefaultReasoningEffort()
        {
            var handler = new CapturingHandler((request, call) => Success("plain"));
            using (var client = new AiClient(new TestLogger(), handler))
            {
                var config = CreateConfig(500);
                config.ModelPriority[0].ReasoningEffort = "default";
                var messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = "hello" }
                };

                AiResponseResult result = await client.SendAsync(config, messages, CancellationToken.None);

                Assert.True(result.Success);
                JObject json = JObject.Parse(handler.Bodies[0]);
                Assert.Null(json["reasoning_effort"]);
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

        private static AppConfig CreateVisionConfig()
        {
            return new AppConfig
            {
                ModelPriority = new List<ModelEntry>
                {
                    new ModelEntry
                    {
                        Name = "Vision endpoint",
                        BaseUrl = "https://vision.test/v1",
                        Model = "vision-model",
                        Capability = ModelCapability.Vision
                    }
                },
                MultiRequestMode = MultiRequestMode.Sequential,
                MultiRequestTimeoutMs = 500,
                HistoryLimit = 10,
                SystemPrompt = "You are a helpful assistant."
            }.Normalize();
        }

        private static HttpResponseMessage Success(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"" + content + "\"}}]}",
                    Encoding.UTF8,
                    "application/json")
            };
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

        private sealed class CapturingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responseFactory;
            private int _calls;

            public List<string> Bodies { get; } = new List<string>();

            public CapturingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory)
            {
                _responseFactory = responseFactory;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _calls++;
                Bodies.Add(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
                return _responseFactory(request, _calls);
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
