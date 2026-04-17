using System;
using System.Windows.Forms;
using Suterusu.Application;
using Suterusu.Bootstrap;
using Suterusu.Configuration;
using Suterusu.Services;
using Suterusu.UI;

namespace Suterusu
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Parse startup arguments first (before any console allocation)
            StartupOptions options = StartupOptions.Parse(args);

            // Configure console visibility
            if (options.DebugEnabled)
                ConsoleManager.AllocDebugConsole();
            else
                ConsoleManager.FreeHeadlessConsole();

            // One-time global NLog setup (no file logging in --open-settings mode)
            NLogLogger.Configure(options.DebugEnabled, !options.OpenSettings);

            // --open-settings: show WPF settings window directly on this [STAThread].
            // ShowDialog() pushes WPF's own DispatcherFrame, giving full keyboard support
            // on every PC without any WinForms interop. No WinForms loop needed.
            if (options.OpenSettings)
            {
                try
                {
                    var configManager = new ConfigManager(new NLogLogger("Suterusu.Config"));
                    configManager.LoadOrCreateDefault();
                    new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                    new SettingsWindow(configManager).ShowDialog();
                }
                catch (Exception ex)
                {
                    new NLogLogger("Suterusu.Program").Error("Settings window failed.", ex);
                }
                return;
            }

            // Required for WinForms DPI awareness and message loop
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                var context = new HeadlessApplicationContext(options);
                System.Windows.Forms.Application.Run(context);
            }
            catch (Exception ex)
            {
                new NLogLogger("Suterusu.Program").Error("An unhandled exception has occurred.", ex);
                if (options.DebugEnabled)
                {
                    MessageBox.Show(
                        $"Fatal error:\n\n{ex}",
                        "Suterusu – Fatal Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
    }
}
