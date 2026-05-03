using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Suterusu.Configuration;

namespace Suterusu.Services
{
    public sealed class CdpScriptInjector
    {
        private readonly ILogger _logger;

        public CdpScriptInjector(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<int> InjectStartupScriptsAsync(CdpClient client, CdpSettings settings, CancellationToken cancellationToken)
        {
            if (!settings.InjectOnStartup)
                return 0;

            string directory = ResolveDirectory(settings.StartupScriptsDirectory);
            if (!Directory.Exists(directory))
            {
                _logger.Warn("CDP event scripts directory not found: " + directory);
                return 0;
            }

            string[] eventDirectories = Directory.GetDirectories(directory, "on*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedEventDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (eventDirectories.Length == 0)
            {
                _logger.Warn("No CDP event folders found under: " + directory + " (expected onload, onclick, onkeydown, etc.)");
                return 0;
            }

            int injected = 0;
            foreach (string eventDirectory in eventDirectories)
            {
                string eventName = Path.GetFileName(eventDirectory).Substring(2).ToLowerInvariant();
                string[] scripts = Directory.GetFiles(eventDirectory, "*.js", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (string script in scripts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (await client.InjectEventScriptAsync(script, eventName, cancellationToken).ConfigureAwait(false))
                        {
                            injected++;
                            _logger.Info("Injected CDP on" + eventName + " script: " + Path.GetFileName(script));
                        }
                        else
                        {
                            _logger.Warn("Failed to inject CDP on" + eventName + " script: " + Path.GetFileName(script));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("CDP on" + eventName + " script injection crashed: " + Path.GetFileName(script), ex);
                    }
                }
            }

            _logger.Info("Injected " + injected + " CDP event script(s).");
            return injected;
        }

        public string GetStartupScriptsSignature(CdpSettings settings)
        {
            if (settings == null || !settings.InjectOnStartup)
                return "disabled";

            string directory = ResolveDirectory(settings.StartupScriptsDirectory);
            if (!Directory.Exists(directory))
                return "missing|" + directory;

            string[] eventDirectories = Directory.GetDirectories(directory, "on*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedEventDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (eventDirectories.Length == 0)
                return "empty|" + directory;

            var builder = new StringBuilder();
            foreach (string eventDirectory in eventDirectories)
            {
                string eventName = Path.GetFileName(eventDirectory).Substring(2).ToLowerInvariant();
                string[] scripts = Directory.GetFiles(eventDirectory, "*.js", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (string script in scripts)
                {
                    builder.Append(eventName)
                        .Append('|')
                        .Append(Path.GetFullPath(script))
                        .Append('|')
                        .Append(ComputeFileHash(script))
                        .AppendLine();
                }
            }

            return builder.Length == 0 ? "empty|" + directory : builder.ToString();
        }

        private static bool IsSupportedEventDirectory(string directory)
        {
            string name = Path.GetFileName(directory) ?? "";
            if (!name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || name.Length <= 2)
                return false;

            return Regex.IsMatch(name.Substring(2), "^[A-Za-z][A-Za-z0-9_]*$");
        }

        private static string ResolveDirectory(string configuredDirectory)
        {
            if (Path.IsPathRooted(configuredDirectory))
                return configuredDirectory;

            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredDirectory));
        }

        private static string ComputeFileHash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    builder.Append(b.ToString("x2"));

                return builder.ToString();
            }
        }
    }
}
