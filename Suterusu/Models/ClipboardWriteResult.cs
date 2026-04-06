namespace Suterusu.Models
{
    public class ClipboardWriteResult
    {
        public bool   Success { get; }
        public string Error   { get; }

        private ClipboardWriteResult(bool success, string error)
        {
            Success = success;
            Error   = error;
        }

        public static ClipboardWriteResult Ok()
            => new ClipboardWriteResult(true, null);

        public static ClipboardWriteResult Fail(string error)
            => new ClipboardWriteResult(false, error);
    }
}
