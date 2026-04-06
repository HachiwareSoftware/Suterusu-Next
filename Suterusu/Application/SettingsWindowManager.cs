using System;
using Suterusu.Configuration;
using Suterusu.Services;
using Suterusu.UI;

namespace Suterusu.Application
{
    /// <summary>
    /// Manages a single SettingsWindow instance – reuses it if already open.
    /// Raises <see cref="Closed"/> when the window is dismissed.
    /// </summary>
    public class SettingsWindowManager
    {
        private readonly ConfigManager         _configManager;
        private readonly ClipboardAiController _controller;
        private readonly ILogger               _logger = new NLogLogger("Suterusu.Settings");

        private SettingsWindow _window;

        /// <summary>Raised when the settings window is closed.</summary>
        public event EventHandler Closed;

        public SettingsWindowManager(ConfigManager configManager, ClipboardAiController controller)
        {
            _configManager = configManager;
            _controller    = controller;
        }

        public void ShowSettings()
        {
            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                return;
            }

            _window = new SettingsWindow(_configManager, _controller);
            _window.Closed += OnSettingsClosed;
            _window.Show();
            _logger.Info("Settings window opened.");
        }

        private void OnSettingsClosed(object sender, EventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
