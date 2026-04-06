using System;
using System.Windows.Forms;
using Suterusu.Application;
using Suterusu.Bootstrap;
using Suterusu.Services;

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

            // One-time global NLog setup (console only in --debug mode)
            NLogLogger.Configure(options.DebugEnabled);

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
