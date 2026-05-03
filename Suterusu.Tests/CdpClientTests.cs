using System.Collections.Generic;
using Suterusu.Models;
using Suterusu.Services;
using Xunit;

namespace Suterusu.Tests
{
    public class CdpClientTests
    {
        [Fact]
        public void SelectTarget_WithoutPattern_ReturnsFirstPageTarget()
        {
            var targets = new List<CdpTargetInfo>
            {
                new CdpTargetInfo { Id = "ignored", Type = "service_worker", WebSocketDebuggerUrl = "ws://ignored" },
                new CdpTargetInfo { Id = "page1", Type = "page", Url = "https://example.com", WebSocketDebuggerUrl = "ws://page1" },
                new CdpTargetInfo { Id = "page2", Type = "page", Url = "https://chatgpt.com", WebSocketDebuggerUrl = "ws://page2" }
            };

            var selected = CdpClient.SelectTarget(targets, "");

            Assert.Equal("ws://page1", selected.WebSocketDebuggerUrl);
        }

        [Fact]
        public void SelectTarget_WithoutPattern_SkipsDevToolsTarget()
        {
            var targets = new List<CdpTargetInfo>
            {
                new CdpTargetInfo { Id = "devtools", Type = "page", Url = "devtools://devtools/bundled/devtools_app.html", WebSocketDebuggerUrl = "ws://devtools" },
                new CdpTargetInfo { Id = "page", Type = "page", Url = "https://example.com", WebSocketDebuggerUrl = "ws://page" }
            };

            var selected = CdpClient.SelectTarget(targets, "");

            Assert.Equal("ws://page", selected.WebSocketDebuggerUrl);
        }

        [Fact]
        public void SelectTarget_WithPattern_ReturnsMatchingPageTarget()
        {
            var targets = new List<CdpTargetInfo>
            {
                new CdpTargetInfo { Id = "page1", Type = "page", Url = "https://example.com", WebSocketDebuggerUrl = "ws://page1" },
                new CdpTargetInfo { Id = "page2", Type = "page", Url = "https://chatgpt.com", WebSocketDebuggerUrl = "ws://page2" }
            };

            var selected = CdpClient.SelectTarget(targets, "chatgpt\\.com");

            Assert.Equal("ws://page2", selected.WebSocketDebuggerUrl);
        }

        [Fact]
        public void SelectTarget_WithInvalidPattern_ReturnsNull()
        {
            var targets = new List<CdpTargetInfo>
            {
                new CdpTargetInfo { Id = "page", Type = "page", Url = "https://chatgpt.com", WebSocketDebuggerUrl = "ws://page" }
            };

            var selected = CdpClient.SelectTarget(targets, "[");

            Assert.Null(selected);
        }

        [Fact]
        public void SelectTargets_WithoutPattern_ReturnsAllInjectablePageTargets()
        {
            var targets = new List<CdpTargetInfo>
            {
                new CdpTargetInfo { Id = "page1", Type = "page", Url = "https://example.com", WebSocketDebuggerUrl = "ws://page1" },
                new CdpTargetInfo { Id = "devtools", Type = "page", Url = "devtools://devtools/bundled/devtools_app.html", WebSocketDebuggerUrl = "ws://devtools" },
                new CdpTargetInfo { Id = "page2", Type = "page", Url = "https://chatgpt.com", WebSocketDebuggerUrl = "ws://page2" }
            };

            var selected = CdpClient.SelectTargets(targets, "");

            Assert.Equal(2, selected.Count);
            Assert.Equal("ws://page1", selected[0].WebSocketDebuggerUrl);
            Assert.Equal("ws://page2", selected[1].WebSocketDebuggerUrl);
        }
    }
}
