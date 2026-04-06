namespace Suterusu.Models
{
    public class AiResponseResult
    {
        public bool   Success    { get; }
        public string Content    { get; }
        public string ModelUsed  { get; }
        public string Error      { get; }

        private AiResponseResult(bool success, string content, string modelUsed, string error)
        {
            Success   = success;
            Content   = content;
            ModelUsed = modelUsed;
            Error     = error;
        }

        public static AiResponseResult Ok(string content, string modelUsed)
            => new AiResponseResult(true, content, modelUsed, null);

        public static AiResponseResult Fail(string error)
            => new AiResponseResult(false, null, null, error);
    }
}
