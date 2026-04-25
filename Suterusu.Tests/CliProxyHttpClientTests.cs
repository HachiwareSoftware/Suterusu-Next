using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Suterusu.Configuration;
using Suterusu.Services;
using Xunit;

namespace Suterusu.Tests
{
    public class CliProxyHttpClientTests
    {
        [Fact]
        public async Task GetModelsAsync_UsesBracketedIpv6LoopbackUrl()
        {
            var handler = new RecordingHandler();
            var config = AppConfig.CreateDefault();
            config.CliProxy.Host = "::1";
            config.CliProxy.Port = 8317;

            using (var client = new CliProxyHttpClient(new NLogLogger("Suterusu.Tests.CliProxy"), handler))
            {
                var result = await client.GetModelsAsync(config, CancellationToken.None);

                Assert.True(result.Success);
                Assert.Equal("http://[::1]:8317/v1/models", handler.LastRequestUri.AbsoluteUri);
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public Uri LastRequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[{\"id\":\"gpt-5.3-codex\"}]}")
                });
            }
        }
    }
}
