namespace Suterusu.Models
{
    public class SaveConfigResult
    {
        public bool   Success { get; }
        public string Error   { get; }

        private SaveConfigResult(bool success, string error)
        {
            Success = success;
            Error   = error;
        }

        public static SaveConfigResult Ok()
            => new SaveConfigResult(true, null);

        public static SaveConfigResult Fail(string error)
            => new SaveConfigResult(false, error);
    }
}
