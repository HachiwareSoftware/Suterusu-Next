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

        [Fact]
        public async Task GetModelsAsync_ReturnsRootCauseForConnectionFailure()
        {
            var handler = new ThrowingHandler(new HttpRequestException(
                "An error occurred while sending the request.",
                new InvalidOperationException("No connection could be made.")));
            var config = AppConfig.CreateDefault();

            using (var client = new CliProxyHttpClient(new NLogLogger("Suterusu.Tests.CliProxy"), handler))
            {
                var result = await client.GetModelsAsync(config, CancellationToken.None, false);

                Assert.False(result.Success);
                Assert.Equal("No connection could be made.", result.Error);
            }
        }

        [Fact]
        public async Task GetModelsAsync_RethrowsCallerCancellation()
        {
            var handler = new CanceledHandler();
            var config = AppConfig.CreateDefault();

            using (var cts = new CancellationTokenSource())
            using (var client = new CliProxyHttpClient(new NLogLogger("Suterusu.Tests.CliProxy"), handler))
            {
                cts.Cancel();

                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => client.GetModelsAsync(config, cts.Token, false));
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

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _exception;

            public ThrowingHandler(Exception exception)
            {
                _exception = exception;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw _exception;
            }
        }

        private sealed class CanceledHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }
    }
}
