using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Suterusu.Configuration;
using Suterusu.Services;
using Xunit;

namespace Suterusu.Tests
{
    public class CliProxyReleaseServiceTests
    {
        // ── SelectWindowsAsset ────────────────────────────────────────────────

        [Fact]
        public void SelectWindowsAsset_Amd64_ReturnsMatchingName()
        {
            var names = new[]
            {
                "CLIProxyAPI_6.9.40_linux_amd64.tar.gz",
                "CLIProxyAPI_6.9.40_windows_amd64.zip",
                "CLIProxyAPI_6.9.40_windows_arm64.zip"
            };

            string result = CliProxyReleaseService.SelectWindowsAsset("amd64", names);

            Assert.Equal("CLIProxyAPI_6.9.40_windows_amd64.zip", result);
        }

        [Fact]
        public void SelectWindowsAsset_Arm64_ReturnsMatchingName()
        {
            var names = new[]
            {
                "CLIProxyAPI_6.9.40_windows_amd64.zip",
                "CLIProxyAPI_6.9.40_windows_arm64.zip"
            };

            string result = CliProxyReleaseService.SelectWindowsAsset("arm64", names);

            Assert.Equal("CLIProxyAPI_6.9.40_windows_arm64.zip", result);
        }

        [Fact]
        public void SelectWindowsAsset_NoneMatch_ReturnsNull()
        {
            var names = new[]
            {
                "CLIProxyAPI_6.9.40_linux_amd64.tar.gz",
                "CLIProxyAPI_6.9.40_darwin_arm64.tar.gz"
            };

            string result = CliProxyReleaseService.SelectWindowsAsset("amd64", names);

            Assert.Null(result);
        }

        [Fact]
        public void SelectWindowsAsset_NullList_ReturnsNull()
        {
            Assert.Null(CliProxyReleaseService.SelectWindowsAsset("amd64", null));
        }

        // ── ExtractSha256FromDigest ───────────────────────────────────────────

        [Fact]
        public void ExtractSha256FromDigest_PrefixedValue_StripsPrefix()
        {
            string digest = "sha256:abc123def456";

            string result = CliProxyReleaseService.ExtractSha256FromDigest(digest);

            Assert.Equal("abc123def456", result);
        }

        [Fact]
        public void ExtractSha256FromDigest_NoPrefixValue_ReturnsAsIs()
        {
            string result = CliProxyReleaseService.ExtractSha256FromDigest("abc123");

            Assert.Equal("abc123", result);
        }

        [Fact]
        public void ExtractSha256FromDigest_NullOrWhitespace_ReturnsNull()
        {
            Assert.Null(CliProxyReleaseService.ExtractSha256FromDigest(null));
            Assert.Null(CliProxyReleaseService.ExtractSha256FromDigest("   "));
        }

        // ── ComputeSha256Hex ──────────────────────────────────────────────────

        [Fact]
        public void ComputeSha256Hex_EmptyData_ReturnsKnownHash()
        {
            // SHA-256 of empty bytes
            const string expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            string actual = CliProxyReleaseService.ComputeSha256Hex(new byte[0]);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ComputeSha256Hex_KnownData_MatchesExpected()
        {
            byte[] data = Encoding.UTF8.GetBytes("hello");
            // SHA-256("hello")
            const string expected = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
            string actual = CliProxyReleaseService.ComputeSha256Hex(data);
            Assert.Equal(expected, actual);
        }

        // ── TryExtractOAuthUrl ────────────────────────────────────────────────

        [Fact]
        public void TryExtractOAuthUrl_LineWithUrl_ExtractsUrl()
        {
            string line = "Open this URL: https://chatgpt.com/auth/login?redirect=xxx";

            bool result = CliProxyProcessManager.TryExtractOAuthUrl(line, out string url);

            Assert.True(result);
            Assert.Equal("https://chatgpt.com/auth/login?redirect=xxx", url);
        }

        [Fact]
        public void TryExtractOAuthUrl_BareUrlLine_ExtractsUrl()
        {
            string line = "https://auth.openai.com/oauth/token?code=abc123";

            bool result = CliProxyProcessManager.TryExtractOAuthUrl(line, out string url);

            Assert.True(result);
            Assert.Equal("https://auth.openai.com/oauth/token?code=abc123", url);
        }

        [Fact]
        public void TryExtractOAuthUrl_NoUrl_ReturnsFalse()
        {
            bool result = CliProxyProcessManager.TryExtractOAuthUrl("Starting server...", out string url);

            Assert.False(result);
            Assert.Null(url);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryExtractOAuthUrl_EmptyOrNull_ReturnsFalse(string line)
        {
            bool result = CliProxyProcessManager.TryExtractOAuthUrl(line, out string url);

            Assert.False(result);
            Assert.Null(url);
        }

        [Fact]
        public void TryExtractOAuthUrl_UrlWithTrailingWhitespace_Trimmed()
        {
            string line = "Visit: https://example.com/oauth  (expires in 5 minutes)";

            bool result = CliProxyProcessManager.TryExtractOAuthUrl(line, out string url);

            Assert.True(result);
            Assert.Equal("https://example.com/oauth", url);
        }

        // ── GetInstalledVersion ───────────────────────────────────────────────

        [Fact]
        public void GetInstalledVersion_NoVersionFile_ReturnsNull()
        {
            var settings = new CliProxySettings
            {
                RuntimeDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
            };

            var service = new CliProxyReleaseService(new NLogLogger("Suterusu.Tests.Release"));
            string version = service.GetInstalledVersion(settings);

            Assert.Null(version);
        }

        [Fact]
        public void GetInstalledVersion_ValidVersionFile_ReturnsVersion()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string versionFile = Path.Combine(tempDir, CliProxyReleaseService.VersionFileName);
                File.WriteAllText(versionFile,
                    "{\"version\":\"v6.9.40\",\"installed_at\":\"2026-04-28T00:00:00Z\"}");

                var settings = new CliProxySettings { RuntimeDirectory = tempDir };
                var service = new CliProxyReleaseService(new NLogLogger("Suterusu.Tests.Release"));

                string version = service.GetInstalledVersion(settings);

                Assert.Equal("v6.9.40", version);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        // ── ZIP path traversal prevention ─────────────────────────────────────

        [Fact]
        public void ExtractSelectedFiles_PathTraversalEntry_IsSkipped()
        {
            // Build a zip that contains a path-traversal entry
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string binDir  = Path.Combine(tempDir, "bin");
            string zipPath = Path.Combine(tempDir, "test.zip");
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(binDir);

            try
            {
                // Create zip with a traversal entry name
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    // Normal entry that should be extracted
                    var goodEntry = zip.CreateEntry("cli-proxy-api.exe");
                    using (var s = goodEntry.Open())
                    using (var w = new StreamWriter(s))
                        w.Write("fake exe");

                    // Traversal entry that should NOT be extracted
                    var evilEntry = zip.CreateEntry("../evil.txt");
                    using (var s = evilEntry.Open())
                    using (var w = new StreamWriter(s))
                        w.Write("pwned");
                }

                // Use reflection to call the private static ExtractSelectedFiles
                var method = typeof(CliProxyReleaseService).GetMethod(
                    "ExtractSelectedFiles",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                method.Invoke(null, new object[] { zipPath, binDir, tempDir });

                // The traversal file must not have been created outside tempDir
                string evilPath = Path.GetFullPath(Path.Combine(tempDir, "..", "evil.txt"));
                Assert.False(File.Exists(evilPath),
                    "Path traversal entry should not be extracted outside the target directory.");

                // The legitimate file should exist
                Assert.True(File.Exists(Path.Combine(binDir, "cli-proxy-api.exe")));
            }
            finally
            {
                Directory.Delete(tempDir, true);
                // Clean up potential traversal artifact
                string evilPath = Path.GetFullPath(
                    Path.Combine(Path.GetTempPath(), "evil.txt"));
                if (File.Exists(evilPath)) File.Delete(evilPath);
            }
        }
    }
}
