using System.Linq;
using Suterusu.Configuration;
using Suterusu.Services;
using Xunit;

namespace Suterusu.Tests
{
    public class CliProxyProcessManagerTests
    {
        [Fact]
        public void BuildLoginArguments_UsesCodexLoginByDefault()
        {
            var settings = CliProxySettings.CreateDefault();

            var args = CliProxyProcessManager.BuildLoginArguments(settings).ToList();

            Assert.Contains("--codex-login", args);
            Assert.DoesNotContain("--login", args);
            Assert.Contains("--no-browser", args);
            Assert.Contains(settings.ConfigPath, args);
        }

        [Fact]
        public void BuildLoginArguments_UsesGeminiLogin()
        {
            var settings = CliProxySettings.CreateDefault();
            settings.Provider = CliProxySettings.GeminiProvider;

            var args = CliProxyProcessManager.BuildLoginArguments(settings).ToList();

            Assert.Contains("--login", args);
            Assert.DoesNotContain("--codex-login", args);
            Assert.DoesNotContain("--project_id", args);
        }

        [Fact]
        public void BuildLoginArguments_AddsGeminiProjectId_WhenConfigured()
        {
            var settings = CliProxySettings.CreateDefault();
            settings.Provider = CliProxySettings.GeminiProvider;
            settings.GeminiProjectId = "  test-project  ";

            var args = CliProxyProcessManager.BuildLoginArguments(settings).ToList();

            int index = args.IndexOf("--project_id");
            Assert.True(index >= 0);
            Assert.Equal("test-project", args[index + 1]);
        }
    }
}
