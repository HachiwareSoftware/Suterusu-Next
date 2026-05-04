using System;
using System.IO;
using Suterusu.Services;
using Xunit;

namespace Suterusu.Tests
{
    public class OneOcrRuntimeLocatorTests : IDisposable
    {
        private readonly string _root;

        public OneOcrRuntimeLocatorTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "suterusu-oneocr-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [Fact]
        public void ValidateRuntimePath_ExplicitValidFolder_Succeeds()
        {
            string runtime = CreateRuntimeFolder(Path.Combine(_root, "runtime"));

            var result = OneOcrRuntimeLocator.ValidateRuntimePath(runtime, explicitPath: true);

            Assert.True(result.Success);
            Assert.Equal(runtime, result.RuntimePath);
        }

        [Fact]
        public void ValidateRuntimePath_MissingDependency_ReturnsClearError()
        {
            string runtime = Path.Combine(_root, "runtime");
            Directory.CreateDirectory(runtime);
            File.WriteAllText(Path.Combine(runtime, OneOcrRuntimeLocator.OneOcrDllName), string.Empty);

            var result = OneOcrRuntimeLocator.ValidateRuntimePath(runtime, explicitPath: true);

            Assert.False(result.Success);
            Assert.Contains(OneOcrRuntimeLocator.OneOcrModelName, result.Error);
            Assert.Contains(OneOcrRuntimeLocator.OnnxRuntimeDllName, result.Error);
        }

        [Fact]
        public void Resolve_BlankPath_SelectsNewestValidScreenSketchRuntime()
        {
            string programFiles = Path.Combine(_root, "ProgramFiles");
            string oldRuntime = CreateScreenSketchRuntime(programFiles, "11.1.0.0");
            string newRuntime = CreateScreenSketchRuntime(programFiles, "11.9.0.0");

            var result = OneOcrRuntimeLocator.Resolve(string.Empty, programFiles);

            Assert.True(result.Success);
            Assert.Equal(newRuntime, result.RuntimePath);
            Assert.NotEqual(oldRuntime, result.RuntimePath);
        }

        [Fact]
        public async System.Threading.Tasks.Task OneOcrClient_MissingRuntime_ReturnsFailure()
        {
            var client = new OneOcrClient(new StubLogger(), Path.Combine(_root, "missing"));

            var result = await client.RunOcrAsync(new byte[] { 1, 2, 3 }, string.Empty, 1000, System.Threading.CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("OneOCR runtime folder", result.Error);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test folders.
            }
        }

        private static string CreateScreenSketchRuntime(string programFiles, string version)
        {
            string package = Path.Combine(programFiles, "WindowsApps", "Microsoft.ScreenSketch_" + version + "_x64__8wekyb3d8bbwe");
            return CreateRuntimeFolder(Path.Combine(package, "SnippingTool"));
        }

        private static string CreateRuntimeFolder(string runtime)
        {
            Directory.CreateDirectory(runtime);
            File.WriteAllText(Path.Combine(runtime, OneOcrRuntimeLocator.OneOcrDllName), string.Empty);
            File.WriteAllText(Path.Combine(runtime, OneOcrRuntimeLocator.OneOcrModelName), string.Empty);
            File.WriteAllText(Path.Combine(runtime, OneOcrRuntimeLocator.OnnxRuntimeDllName), string.Empty);
            return runtime;
        }

        private sealed class StubLogger : ILogger
        {
            public void Debug(string message) { }
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }
}
