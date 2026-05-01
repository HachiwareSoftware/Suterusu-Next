using System;
using System.IO;
using System.Text;

namespace Suterusu.Services
{
    public sealed class CdpFileLogger : ILogger
    {
        private readonly object _lock = new object();
        private readonly string _filePath;
        private readonly ILogger _forwardLogger;

        public CdpFileLogger()
        {
            string logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(logDir);
            _filePath = Path.Combine(logDir, "cdp-" + DateTime.Now.ToString("yyMMdd-HHmmss") + ".log");
            _forwardLogger = new NLogLogger("Suterusu.CDP");
        }

        public void Debug(string message) => Write("DEBUG", message, null);

        public void Info(string message) => Write("INFO", message, null);

        public void Warn(string message) => Write("WARN", message, null);

        public void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private void Write(string level, string message, Exception ex)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + " [Suterusu.CDP] [" + level + "]: "
                + (message ?? "");

            if (ex != null)
                line += Environment.NewLine + ex;

            lock (_lock)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }

            Forward(level, message, ex);
        }

        private void Forward(string level, string message, Exception ex)
        {
            try
            {
                switch (level)
                {
                    case "DEBUG":
                        _forwardLogger.Debug(message);
                        break;
                    case "INFO":
                        _forwardLogger.Info(message);
                        break;
                    case "WARN":
                        _forwardLogger.Warn(message);
                        break;
                    case "ERROR":
                        _forwardLogger.Error(message, ex);
                        break;
                }
            }
            catch
            {
                // CDP logging must never affect app lifetime.
            }
        }
    }
}
