using System;
using System.IO;
using System.Security.Cryptography;

namespace Suterusu.Configuration
{
    public class CliProxySettings
    {
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
        public string Model { get; set; }
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
                Model = "gpt-5.3-codex",
                OAuthCallbackPort = 1455
            };
        }

        public static string GetDefaultRuntimeDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = AppDomain.CurrentDomain.BaseDirectory;

            return Path.Combine(localAppData, "Suterusu", "CLIProxyAPI");
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
    }
}
