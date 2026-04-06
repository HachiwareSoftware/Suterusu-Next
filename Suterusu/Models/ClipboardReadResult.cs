namespace Suterusu.Models
{
    public class ClipboardReadResult
    {
        public bool   Success { get; }
        public string Text    { get; }
        public string Error   { get; }

        private ClipboardReadResult(bool success, string text, string error)
        {
            Success = success;
            Text    = text;
            Error   = error;
        }

        public static ClipboardReadResult Ok(string text)
            => new ClipboardReadResult(true, text, null);

        public static ClipboardReadResult Fail(string error)
            => new ClipboardReadResult(false, null, error);
    }
}
