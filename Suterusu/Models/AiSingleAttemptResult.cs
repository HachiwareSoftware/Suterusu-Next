namespace Suterusu.Models
{
    public class AiSingleAttemptResult
    {
        public bool   Success { get; }
        public string Content { get; }
        public string Error   { get; }

        private AiSingleAttemptResult(bool success, string content, string error)
        {
            Success = success;
            Content = content;
            Error   = error;
        }

        public static AiSingleAttemptResult Ok(string content)
            => new AiSingleAttemptResult(true, content, null);

        public static AiSingleAttemptResult Fail(string error)
            => new AiSingleAttemptResult(false, null, error);
    }
}
