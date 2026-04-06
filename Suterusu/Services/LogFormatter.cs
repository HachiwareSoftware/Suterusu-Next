using System;

namespace Suterusu.Services
{
    public static class LogFormatter
    {
        public static string Format(DateTime timestamp, LogLevel level, string loggerName, string message)
        {
            string levelText = level.ToString().ToUpperInvariant().PadRight(5);
            string name = string.IsNullOrWhiteSpace(loggerName) ? "Suterusu" : loggerName;
            string text = message ?? string.Empty;

            return string.Format(
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2} - {3}",
                timestamp,
                levelText,
                name,
                text);
        }
    }
}
