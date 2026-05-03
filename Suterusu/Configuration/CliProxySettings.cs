using System;
using System.IO;
using System.Security.Cryptography;

namespace Suterusu.Configuration
{
    public class CliProxySettings
    {
        public const string GeneratedModelEntryName = "ChatGPT (CLIProxyAPI)";
        public const string GeminiModelEntryName = "Gemini (CLIProxyAPI)";
        public const string DefaultCodexModel = "gpt-5.3-codex";
        public const string DefaultGeminiModel = "gemini-3.1-flash-lite-preview";
        public const string LegacyGemini3FlashModel = "gemini-3-flash";
        public const string LegacyGeminiProModel = "gemini-2.5-pro";
        public const string LegacyGeminiFlashModel = "gemini-2.5-flash";
        public const string GeminiProvider = "gemini";
        public const string CodexProvider = "codex";

        public bool Enabled { get; set; }
        public bool AutoStart { get; set; }
        public string ExecutablePath { get; set; }
        public string RuntimeDirectory { get; set; }
        public string ConfigPath { get; set; }
        public string AuthDirectory { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string ApiKey { get; set; }
        public string ManagementKey { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string GeminiProjectId { get; set; }
        public int OAuthCallbackPort { get; set; }

        public static CliProxySettings CreateDefault()
        {
            string runtimeDirectory = GetDefaultRuntimeDirectory();

            return new CliProxySettings
            {
                Enabled = false,
                AutoStart = true,
                RuntimeDirectory = runtimeDirectory,
                ExecutablePath = Path.Combine(runtimeDirectory, "bin", "cli-proxy-api.exe"),
                ConfigPath = Path.Combine(runtimeDirectory, "config.yaml"),
                AuthDirectory = Path.Combine(runtimeDirectory, "auths"),
                Host = "127.0.0.1",
                Port = 8317,
                ApiKey = GenerateSecret(24),
                ManagementKey = GenerateSecret(24),
                Provider = CodexProvider,
                Model = DefaultCodexModel,
                GeminiProjectId = "",
                OAuthCallbackPort = 1455
            };
        }

        public string GetDisplayName()
        {
            return IsGeminiProvider(Provider) ? GeminiModelEntryName : GeneratedModelEntryName;
        }

        public static bool IsGeminiProvider(string provider)
        {
            return string.Equals(provider, GeminiProvider, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCodexProvider(string provider)
        {
            return string.IsNullOrWhiteSpace(provider)
                || string.Equals(provider, CodexProvider, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetDefaultRuntimeDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cliproxy");
        }

        public static string GetLegacyRuntimeDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Suterusu",
                "CLIProxyAPI");
        }

        public static string GetExecutablePath(string runtimeDirectory)
        {
            return Path.Combine(runtimeDirectory, "bin", "cli-proxy-api.exe");
        }

        public static bool IsLegacyRuntimeDirectory(string runtimeDirectory)
        {
            return PathsEqual(runtimeDirectory, GetLegacyRuntimeDirectory());
        }

        public static bool IsLegacyExecutablePath(string executablePath)
        {
            return PathsEqual(executablePath, GetExecutablePath(GetLegacyRuntimeDirectory()));
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            string normalizedLeft = Path.GetFullPath(left.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(right.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        public static string GenerateSecret(int byteCount)
        {
            if (byteCount <= 0)
                byteCount = 24;

            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public string GetApiBaseUrl()
        {
            return BuildBaseUrl(Host, Port) + "/v1";
        }

        public static string BuildBaseUrl(string host, int port)
        {
            string normalizedHost = FormatHostForUrl(host);
            int normalizedPort = port <= 0 ? 8317 : port;
            return $"http://{normalizedHost}:{normalizedPort}";
        }

        private static string FormatHostForUrl(string host)
        {
            string effectiveHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            if (effectiveHost.Contains(":")
                && !effectiveHost.StartsWith("[", StringComparison.Ordinal)
                && !effectiveHost.EndsWith("]", StringComparison.Ordinal))
            {
                return $"[{effectiveHost}]";
            }

            return effectiveHost;
        }
    }
}
