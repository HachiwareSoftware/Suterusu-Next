using System;
using System.Collections.Generic;

namespace Suterusu.Models
{
    public class CliProxyHealthResult
    {
        public bool Success { get; }
        public string Error { get; }
        public IReadOnlyList<string> Models { get; }

        private CliProxyHealthResult(bool success, string error, IReadOnlyList<string> models)
        {
            Success = success;
            Error = error;
            Models = models ?? Array.Empty<string>();
        }

        public static CliProxyHealthResult Ok(IReadOnlyList<string> models)
        {
            return new CliProxyHealthResult(true, null, models);
        }

        public static CliProxyHealthResult Fail(string error)
        {
            return new CliProxyHealthResult(false, error, null);
        }
    }
}
