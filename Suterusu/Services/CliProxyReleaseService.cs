using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class CliProxyReleaseService : IDisposable
    {
        private const string GitHubApiUrl =
            "https://api.github.com/repos/router-for-me/CLIProxyAPI/releases/latest";

        public const string VersionFileName = "version.json";

        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsClient;

        public CliProxyReleaseService(ILogger logger)
            : this(logger, null)
        {
        }

        public CliProxyReleaseService(ILogger logger, HttpClient httpClient)
        {
            _logger = logger;
            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsClient = false;
            }
            else
            {
                _httpClient = new HttpClient();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Suterusu");
                _ownsClient = true;
            }
        }

        // ── Public async API ──────────────────────────────────────────────────

        public async Task<CliProxyReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Checking CLIProxyAPI latest release...");
            var response = await _httpClient.GetAsync(GitHubApiUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var release = JObject.Parse(json);
            string version = (string)release["tag_name"];
            var assets = (JArray)release["assets"];

            string arch = GetPreferredArchSuffix();
            var info = SelectAssetFromJson(version, arch, assets)
                    ?? SelectAssetFromJson(version, "amd64", assets);  // fallback to x64

            if (info == null)
                throw new InvalidOperationException(
                    $"No Windows zip asset found in CLIProxyAPI release {version}.");

            return info;
        }

        public async Task<string> GetLatestVersionAsync(CancellationToken cancellationToken)
        {
            var info = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            return info.Version;
        }

        public string GetInstalledVersion(CliProxySettings settings)
        {
            if (settings == null)
                return null;

            string runtimeDir = string.IsNullOrWhiteSpace(settings.RuntimeDirectory)
                ? CliProxySettings.GetDefaultRuntimeDirectory()
                : settings.RuntimeDirectory;

            string versionFile = Path.Combine(runtimeDir, VersionFileName);
            if (!File.Exists(versionFile))
            {
                // Binary present but installed before version tracking started; report as installed.
                string exePath = Path.Combine(runtimeDir, "bin", "cli-proxy-api.exe");
                return File.Exists(exePath) ? "(installed)" : null;
            }

            try
            {
                string raw = File.ReadAllText(versionFile);
                var obj = JObject.Parse(raw);
                return (string)obj["version"];
            }
            catch
            {
                return null;
            }
        }

        public async Task<CliProxyResult> InstallLatestAsync(
            CliProxySettings settings,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (settings == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            if (string.IsNullOrWhiteSpace(settings.RuntimeDirectory))
                settings.RuntimeDirectory = CliProxySettings.GetDefaultRuntimeDirectory();

            if (string.IsNullOrWhiteSpace(settings.ExecutablePath))
                settings.ExecutablePath = Path.Combine(settings.RuntimeDirectory, "bin", "cli-proxy-api.exe");

            try
            {
                progress?.Report("Fetching CLIProxyAPI release info...");
                _logger.Info("Fetching latest CLIProxyAPI release...");

                CliProxyReleaseInfo releaseInfo;
                try
                {
                    releaseInfo = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to get CLIProxyAPI release info.", ex);
                    return CliProxyResult.Fail("Could not fetch CLIProxyAPI release info: " + ex.Message);
                }

                _logger.Info($"Downloading CLIProxyAPI {releaseInfo.Version}...");
                progress?.Report($"Downloading CLIProxyAPI {releaseInfo.Version}...");

                byte[] zipBytes;
                try
                {
                    zipBytes = await DownloadBytesAsync(releaseInfo.AssetUrl, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error("Download failed.", ex);
                    return CliProxyResult.Fail("Download failed: " + ex.Message);
                }

                progress?.Report("Verifying download...");
                if (!string.IsNullOrWhiteSpace(releaseInfo.Sha256Digest))
                {
                    string actual = ComputeSha256Hex(zipBytes);
                    if (!string.Equals(actual, releaseInfo.Sha256Digest, StringComparison.OrdinalIgnoreCase))
                    {
                        return CliProxyResult.Fail(
                            $"SHA-256 verification failed. Expected {releaseInfo.Sha256Digest}, got {actual}.");
                    }
                }

                string tempZip = Path.GetTempFileName() + ".zip";
                try
                {
                    File.WriteAllBytes(tempZip, zipBytes);

                    string binDir = Path.GetDirectoryName(settings.ExecutablePath);
                    Directory.CreateDirectory(binDir);
                    Directory.CreateDirectory(settings.RuntimeDirectory);

                    progress?.Report("Extracting...");
                    ExtractSelectedFiles(tempZip, binDir, settings.RuntimeDirectory);
                    WriteVersionJson(settings.RuntimeDirectory, releaseInfo);
                }
                finally
                {
                    TryDeleteFile(tempZip);
                }

                _logger.Info($"CLIProxyAPI {releaseInfo.Version} installed to {settings.ExecutablePath}");
                progress?.Report($"CLIProxyAPI {releaseInfo.Version} installed.");
                return CliProxyResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return CliProxyResult.Fail("CLIProxyAPI installation was canceled.");
            }
            catch (Exception ex)
            {
                _logger.Error("CLIProxyAPI installation failed.", ex);
                return CliProxyResult.Fail("CLIProxyAPI installation failed: " + ex.Message);
            }
        }

        // ── Static helpers (public for testability) ───────────────────────────

        /// <summary>
        /// Returns the first asset name in <paramref name="assetNames"/> that matches
        /// the Windows zip convention for the given arch suffix (e.g. "amd64" or "arm64").
        /// </summary>
        public static string SelectWindowsAsset(string archSuffix, IEnumerable<string> assetNames)
        {
            if (assetNames == null)
                return null;

            string suffix = $"_windows_{archSuffix}.zip";
            return assetNames.FirstOrDefault(n =>
                n != null && n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Strips the "sha256:" prefix from a GitHub digest field value.
        /// Returns the hex string, or null for blank input.
        /// </summary>
        public static string ExtractSha256FromDigest(string digest)
        {
            if (string.IsNullOrWhiteSpace(digest))
                return null;

            const string prefix = "sha256:";
            return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? digest.Substring(prefix.Length).Trim()
                : digest.Trim();
        }

        /// <summary>
        /// Returns the preferred GitHub asset arch suffix for the current machine.
        /// "arm64" on ARM64 Windows, "amd64" otherwise.
        /// </summary>
        public static string GetPreferredArchSuffix()
        {
            string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
            if (string.IsNullOrWhiteSpace(arch))
                arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

            return !string.IsNullOrWhiteSpace(arch)
                && arch.IndexOf("ARM64", StringComparison.OrdinalIgnoreCase) >= 0
                ? "arm64"
                : "amd64";
        }

        /// <summary>
        /// Computes the lowercase hex SHA-256 of a byte array.
        /// </summary>
        public static string ComputeSha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(data);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    sb.AppendFormat("{0:x2}", b);
                return sb.ToString();
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
        {
            var response = await _httpClient
                .GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        private static void ExtractSelectedFiles(string zipPath, string binDir, string runtimeDir)
        {
            string[] executableNames = { "cli-proxy-api.exe", "cli-proxy-api" };
            string[] metadataNames   = { "LICENSE", "LICENSE.txt", "VERSION.txt" };

            using (var stream = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string name = entry.Name;
                    string targetDir;

                    if (executableNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        targetDir = binDir;
                    else if (metadataNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        targetDir = runtimeDir;
                    else
                        continue;

                    // Path-traversal guard
                    string destPath = Path.GetFullPath(Path.Combine(targetDir, name));
                    string safeBase = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
                    if (!destPath.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
                        continue;

                    using (var src = entry.Open())
                    using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        src.CopyTo(dst);
                    }
                }
            }
        }

        private static void WriteVersionJson(string runtimeDir, CliProxyReleaseInfo info)
        {
            string versionFile = Path.Combine(runtimeDir, VersionFileName);
            var obj = new JObject
            {
                ["version"]      = info.Version,
                ["installed_at"] = DateTime.UtcNow.ToString("o"),
                ["asset_name"]   = info.AssetName,
                ["sha256"]       = info.Sha256Digest ?? string.Empty
            };
            File.WriteAllText(versionFile, obj.ToString());
        }

        private static CliProxyReleaseInfo SelectAssetFromJson(string version, string arch, JArray assets)
        {
            if (assets == null)
                return null;

            string suffix = $"_windows_{arch}.zip";
            foreach (var asset in assets)
            {
                string name = (string)asset["name"];
                if (name == null || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string url    = (string)asset["browser_download_url"];
                string digest = ExtractSha256FromDigest((string)asset["digest"]);
                return new CliProxyReleaseInfo
                {
                    Version      = version,
                    AssetUrl     = url,
                    AssetName    = name,
                    Sha256Digest = digest
                };
            }

            return null;
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best effort */ }
        }

        public void Dispose()
        {
            if (_ownsClient)
                _httpClient.Dispose();
        }
    }
}
