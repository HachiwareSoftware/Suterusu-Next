using System;
using System.IO;
using Suterusu.Models;
using Suterusu.Services;

namespace Suterusu.Configuration
{
    public class ConfigManager
    {
        private readonly ILogger _logger;

        public string ConfigDirectoryPath { get; }
        public string ConfigFilePath      { get; }
        public AppConfig Current          { get; private set; }

        public ConfigManager(ILogger logger)
        {
            _logger             = logger;
            ConfigDirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
            ConfigFilePath      = Path.Combine(ConfigDirectoryPath, "config.json");
        }

        public AppConfig LoadOrCreateDefault()
        {
            if (!File.Exists(ConfigFilePath))
            {
                _logger.Info("config.json not found – creating defaults.");
                Current = AppConfig.CreateDefault().Normalize();
                SaveInternal(Current);
                return Current;
            }

            try
            {
                string json = File.ReadAllText(ConfigFilePath);
                var config = JsonSettings.Deserialize<AppConfig>(json) ?? AppConfig.CreateDefault();
                Current = config.Normalize();
                _logger.Info("config.json loaded.");
                return Current;
            }
            catch (Exception ex)
            {
                _logger.Error("config.json is corrupt – backing up and recreating defaults.", ex);
                BackupCorruptConfig();
                Current = AppConfig.CreateDefault().Normalize();
                SaveInternal(Current);
                return Current;
            }
        }

        public SaveConfigResult Save(AppConfig config)
        {
            try
            {
                var errors = config.Validate();
                if (errors.Count > 0)
                    return SaveConfigResult.Fail(string.Join("; ", errors));

                config.Normalize();
                SaveInternal(config);
                Current = config;
                return SaveConfigResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save config.json.", ex);
                return SaveConfigResult.Fail(ex.Message);
            }
        }

        private void SaveInternal(AppConfig config)
        {
            string json = JsonSettings.Serialize(config);
            File.WriteAllText(ConfigFilePath, json);
            _logger.Info("config.json saved.");
        }

        private void BackupCorruptConfig()
        {
            try
            {
                string backup = ConfigFilePath + ".bak." + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(ConfigFilePath, backup, overwrite: true);
                _logger.Warn($"Corrupt config backed up to {backup}");
            }
            catch { /* best-effort */ }
        }
    }
}
