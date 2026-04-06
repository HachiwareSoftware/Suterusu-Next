using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Suterusu.Services
{
    /// <summary>
    /// Logger wrapper around NLog.
    /// Format: yyyy-MM-dd HH:mm:ss [name] [LEVEL]: message
    /// Call NLogLogger.Configure() once at startup before creating any loggers.
    /// </summary>
    public class NLogLogger : ILogger
    {
        private static bool _configured;
        private static readonly object _configLock = new object();

        private readonly Logger _log;

        public NLogLogger(string name)
        {
            _log = LogManager.GetLogger(name);
        }

        /// <summary>
        /// One-time global NLog setup. Must be called before instantiating any NLogLogger.
        /// </summary>
        public static void Configure(bool consoleEnabled)
        {
            lock (_configLock)
            {
                if (_configured)
                    return;

                const string layout =
                    "${date:format=yyyy-MM-dd HH\\:mm\\:ss} [${logger}] [${level:uppercase=true}]: " +
                    "${message}${onexception:${newline}${exception:format=tostring}}";

                string logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                Directory.CreateDirectory(logDir);

                // Fixed filename: suterusu-yyMMdd-HHmmss.log (created once at startup)
                string fileName = "suterusu-" + DateTime.Now.ToString("yyMMdd-HHmmss") + ".log";
                string filePath = Path.Combine(logDir, fileName);

                var config = new LoggingConfiguration();

                var fileTarget = new FileTarget("file")
                {
                    FileName = filePath,
                    Layout = layout,
                    Encoding = System.Text.Encoding.UTF8,
                    KeepFileOpen = true
                };
                config.AddTarget(fileTarget);
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);

                if (consoleEnabled)
                {
                    var consoleTarget = new ColoredConsoleTarget("console")
                    {
                        Layout = layout
                    };
                    config.AddTarget(consoleTarget);
                    config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget);
                }

                LogManager.Configuration = config;
                _configured = true;
            }
        }

        public void Debug(string message) => _log.Debug(message);
        public void Info(string message)  => _log.Info(message);
        public void Warn(string message)  => _log.Warn(message);

        public void Error(string message, Exception ex = null)
        {
            if (ex != null)
                _log.Error(ex, message);
            else
                _log.Error(message);
        }
    }
}
