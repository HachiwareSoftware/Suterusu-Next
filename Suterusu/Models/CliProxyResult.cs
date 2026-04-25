namespace Suterusu.Models
{
    public class CliProxyResult
    {
        public bool Success { get; }
        public string Error { get; }
        public string Message { get; }

        private CliProxyResult(bool success, string error, string message)
        {
            Success = success;
            Error = error;
            Message = message;
        }

        public static CliProxyResult Ok(string message = null)
        {
            return new CliProxyResult(true, null, message);
        }

        public static CliProxyResult Fail(string error)
        {
            return new CliProxyResult(false, error, null);
        }
    }
}
