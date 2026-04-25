using System;
using System.IO;
using System.Text;
using Suterusu.Configuration;
using Suterusu.Models;

namespace Suterusu.Services
{
    public class CliProxyConfigWriter
    {
        private readonly ILogger _logger;

        public CliProxyConfigWriter(ILogger logger)
        {
            _logger = logger;
        }

        public CliProxyResult Write(AppConfig config)
        {
            if (config?.CliProxy == null)
                return CliProxyResult.Fail("CLI proxy settings are not configured.");

            try
            {
                var settings = config.CliProxy;

                if (string.IsNullOrWhiteSpace(settings.ConfigPath))
                    return CliProxyResult.Fail("CLI proxy config path is empty.");

                if (string.IsNullOrWhiteSpace(settings.AuthDirectory))
                    return CliProxyResult.Fail("CLI proxy auth directory is empty.");

                string configDirectory = Path.GetDirectoryName(settings.ConfigPath);
                if (!string.IsNullOrWhiteSpace(configDirectory))
                    Directory.CreateDirectory(configDirectory);

                Directory.CreateDirectory(settings.AuthDirectory);

                var yaml = BuildYaml(settings);
                File.WriteAllText(settings.ConfigPath, yaml, new UTF8Encoding(false));

                _logger.Info($"CLI proxy config written: {settings.ConfigPath}");
                return CliProxyResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to write CLI proxy config.", ex);
                return CliProxyResult.Fail(ex.Message);
            }
        }

        private static string BuildYaml(CliProxySettings settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("host: " + AsYamlString(settings.Host));
            sb.AppendLine("port: " + settings.Port);
            sb.AppendLine("auth-dir: " + AsYamlString(settings.AuthDirectory));
            sb.AppendLine("api-keys:");
            sb.AppendLine("  - " + AsYamlString(settings.ApiKey));
            sb.AppendLine("remote-management:");
            sb.AppendLine("  allow-remote: false");
            sb.AppendLine("  secret-key: " + AsYamlString(settings.ManagementKey));
            sb.AppendLine("  disable-control-panel: true");
            sb.AppendLine("debug: false");
            sb.AppendLine("logging-to-file: true");
            sb.AppendLine("logs-max-total-size-mb: 20");
            sb.AppendLine("usage-statistics-enabled: false");
            sb.AppendLine("request-retry: 2");
            sb.AppendLine("routing:");
            sb.AppendLine("  strategy: \"round-robin\"");
            return sb.ToString();
        }

        private static string AsYamlString(string value)
        {
            if (value == null)
                return "''";

            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
