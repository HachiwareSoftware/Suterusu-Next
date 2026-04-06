namespace Suterusu.Models
{
    public class HotkeyActionResult
    {
        public bool   Success { get; }
        public string Error   { get; }

        private HotkeyActionResult(bool success, string error)
        {
            Success = success;
            Error   = error;
        }

        public static HotkeyActionResult Ok()
            => new HotkeyActionResult(true, null);

        public static HotkeyActionResult Fail(string error)
            => new HotkeyActionResult(false, error);
    }
}
